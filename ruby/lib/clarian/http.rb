# frozen_string_literal: true

require "json"
require "net/http"
require "openssl"
require "uri"

module Clarian
  DEFAULT_BASE_URL = "https://api.clarian.finance"
  RETRY_STATUS = [429, 502, 503, 504].freeze

  # Connection settings plus the request loop. One per client.
  class Http
    attr_reader :api_key, :workspace_id, :base_url, :timeout, :environment, :subaccount_id, :max_retries

    def self.environment_for(api_key)
      return "live" if api_key.start_with?("cl_live_sk_")
      return "sandbox" if api_key.start_with?("cl_test_sk_")

      raise ArgumentError, "Invalid API key format: must start with cl_live_sk_ or cl_test_sk_"
    end

    # transport: callable (method, url, headers, body, timeout) -> [status, body_string, headers_hash]
    # sleeper: callable (seconds); injectable so tests do not wait.
    def initialize(api_key:, workspace_id:, base_url: nil, timeout: 30, subaccount_id: nil, max_retries: 2,
                   transport: nil, sleeper: nil)
      @api_key = api_key
      @workspace_id = workspace_id
      @base_url = (base_url || DEFAULT_BASE_URL).sub(%r{/+\z}, "")
      @timeout = timeout
      @environment = self.class.environment_for(api_key)
      @subaccount_id = subaccount_id
      @max_retries = max_retries
      @transport = transport || method(:net_http)
      @sleeper = sleeper || ->(s) { sleep(s) }
    end

    def with_subaccount(subaccount_id)
      self.class.new(api_key: @api_key, workspace_id: @workspace_id, base_url: @base_url, timeout: @timeout,
                     subaccount_id: subaccount_id, max_retries: @max_retries, transport: @transport, sleeper: @sleeper)
    end

    # Calls /v1/{live|test}/<path> and returns the `data` member of the reply.
    def request(method, path, query: nil, body: nil, idempotency_key: nil, otp_code: nil)
      env = @environment == "live" ? "live" : "test"
      url = "#{@base_url}/v1/#{env}/#{path}#{query_string(query)}"
      headers = { "Authorization" => "Bearer #{@api_key}", "X-Workspace-Id" => @workspace_id }
      headers["Content-Type"] = "application/json" unless body.nil?
      headers["X-Subaccount-Id"] = @subaccount_id if present?(@subaccount_id)
      headers["Idempotency-Key"] = idempotency_key if present?(idempotency_key)
      headers["X-OTP-Code"] = otp_code if present?(otp_code)
      raw = body.nil? ? nil : JSON.generate(Http.reject_floats(body))
      # Only a request that cannot double-apply is retried: reads, deletes, or a create with an idempotency key.
      retryable = %w[GET DELETE].include?(method) || present?(idempotency_key)
      status, text = send_with_retries(method, url, headers, raw, retryable ? @max_retries : 0)
      json = parse(text.to_s)
      obj = json.is_a?(Hash) ? json : {}
      raise ClarianError.new(status, obj) unless (200..299).cover?(status)

      json.is_a?(Hash) && json.key?("data") ? json["data"] : json
    end

    def self.seg(id)
      id.to_s.b.gsub(/[^A-Za-z0-9\-._~]/) { |c| format("%%%02X", c.ord) }
    end

    # Amounts are integer minor units. A Float would silently lose precision, so it is refused.
    def self.reject_floats(value)
      case value
      when Float then raise ArgumentError, "floats are not allowed; send amounts as integer minor units (String or Integer)"
      when Hash then value.transform_values { |v| reject_floats(v) }
      when Array then value.map { |v| reject_floats(v) }
      else value
      end
    end

    private

    def present?(v) = !v.nil? && !v.to_s.empty?

    def query_string(query)
      pairs = (query || {}).select { |_, v| v.is_a?(String) || v.is_a?(Integer) }
      pairs.empty? ? "" : "?#{URI.encode_www_form(pairs)}"
    end

    def parse(text)
      return {} if text.empty?

      JSON.parse(text)
    rescue JSON::ParserError
      { "error" => "invalid_json_response", "message" => text[0, 500] }
    end

    def send_with_retries(method, url, headers, raw, retries_left, attempt = 0)
      status, text, resp_headers = @transport.call(method, url, headers, raw, @timeout)
      return [status, text] if !RETRY_STATUS.include?(status) || retries_left <= 0

      @sleeper.call(backoff(attempt, header(resp_headers, "retry-after")))
      send_with_retries(method, url, headers, raw, retries_left - 1, attempt + 1)
    rescue SystemCallError, IOError, Timeout::Error, SocketError, OpenSSL::SSL::SSLError
      raise if retries_left <= 0

      @sleeper.call(backoff(attempt, nil))
      send_with_retries(method, url, headers, raw, retries_left - 1, attempt + 1)
    end

    def header(h, name)
      (h || {}).find { |k, _| k.to_s.downcase == name }&.last&.then { |v| v.is_a?(Array) ? v.first : v }
    end

    def backoff(attempt, retry_after)
      secs = Float(retry_after, exception: false) if retry_after
      return [secs, 30].min if secs && secs >= 0

      rand * [8.0, 0.5 * (2**attempt)].min
    end

    def net_http(method, url, headers, body, timeout)
      uri = URI(url)
      req = Net::HTTPGenericRequest.new(method, !body.nil?, true, uri.request_uri, headers)
      req.body = body
      resp = Net::HTTP.start(uri.host, uri.port, use_ssl: uri.scheme == "https",
                                                 open_timeout: timeout, read_timeout: timeout) { |h| h.request(req) }
      [resp.code.to_i, resp.body.to_s, resp.to_hash]
    end
  end
end
