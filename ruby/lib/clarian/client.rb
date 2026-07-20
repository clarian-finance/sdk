# frozen_string_literal: true

require "json"
require "net/http"
require "uri"

module Clarian
  BASE_URL_LIVE = "https://api.clarian.finance/functions/v1/api-gateway/live"
  BASE_URL_TEST = "https://api.clarian.finance/functions/v1/api-gateway/test"
  DEFAULT_TIMEOUT = 30

  # Percent-encode a single path segment (encodes "/" like Go url.PathEscape).
  def self.path_escape(value)
    value.to_s.b.gsub(/[^A-Za-z0-9\-._~]/) { |c| format("%%%02X", c.ord) }
  end

  class Client
    def self.path_escape(value)
      Clarian.path_escape(value)
    end

    attr_reader :api_key, :workspace_id, :base_url, :timeout
    attr_reader :cash_in, :cash_out, :balances, :transactions, :wallets, :webhooks, :sandbox

    # transport: callable (method, url, headers, body, timeout) -> [status, body_string]
    def initialize(api_key:, workspace_id:, base_url: nil, timeout: nil, transport: nil)
      raise ArgumentError, "api_key is required" if api_key.nil? || api_key.empty?
      raise ArgumentError, "workspace_id is required" if workspace_id.nil? || workspace_id.empty?

      @api_key = api_key
      @workspace_id = workspace_id
      inferred = api_key.start_with?("cl_test_sk_") ? BASE_URL_TEST : BASE_URL_LIVE
      @base_url = (base_url || inferred).sub(%r{/\z}, "")
      @timeout = timeout.nil? ? DEFAULT_TIMEOUT : timeout
      @transport = transport || method(:default_transport)

      @cash_in = CashInService.new(self)
      @cash_out = CashOutService.new(self)
      @balances = BalancesService.new(self)
      @transactions = TransactionsService.new(self)
      @wallets = WalletsService.new(self)
      @webhooks = WebhooksService.new(self)
      @sandbox = SandboxService.new(self)
    end

    # GET /ping: credential and workspace probe.
    def ping
      request("GET", "/ping")
    end

    # Send an authenticated JSON request. Non-2xx raises Clarian::Error.
    def request(method, path, body: nil, idempotency_key: nil)
      url = "#{@base_url}#{path}"
      headers = {
        "Authorization" => "Bearer #{@api_key}",
        "X-Workspace-Id" => @workspace_id
      }

      raw_body = nil
      if !body.nil?
        raw_body = JSON.generate(body)
        headers["Content-Type"] = "application/json"
      end
      headers["Idempotency-Key"] = idempotency_key if idempotency_key && !idempotency_key.empty?

      status, resp_body = @transport.call(method, url, headers, raw_body, @timeout)
      resp_body = resp_body.to_s

      raise Error.new(status, resp_body) if status < 200 || status >= 300
      return {} if resp_body.empty?

      begin
        parsed = JSON.parse(resp_body)
      rescue JSON::ParserError
        raise Error.new(status, resp_body)
      end

      parsed.is_a?(Hash) ? parsed : { "data" => parsed }
    end

    private

    def default_transport(method, url, headers, body, timeout)
      uri = URI(url)
      http = Net::HTTP.new(uri.host, uri.port)
      http.use_ssl = uri.scheme == "https"
      http.open_timeout = timeout
      http.read_timeout = timeout

      request_uri = uri.request_uri
      req =
        case method.to_s.upcase
        when "GET" then Net::HTTP::Get.new(request_uri)
        when "POST" then Net::HTTP::Post.new(request_uri)
        when "PATCH" then Net::HTTP::Patch.new(request_uri)
        when "DELETE" then Net::HTTP::Delete.new(request_uri)
        when "PUT" then Net::HTTP::Put.new(request_uri)
        else
          raise ArgumentError, "unsupported HTTP method: #{method}"
        end

      headers.each { |k, v| req[k] = v }
      req.body = body if body

      res = http.request(req)
      [res.code.to_i, res.body.to_s]
    end
  end

  class CashInService
    def initialize(client)
      @client = client
    end

    # POST /cash-in/pix: generate a dynamic PIX charge.
    def create(params, idempotency_key:)
      @client.request("POST", "/cash-in/pix", body: params, idempotency_key: idempotency_key)
    end

    # GET /cash-in/{id}.
    def retrieve(order_id)
      @client.request("GET", "/cash-in/#{Client.path_escape(order_id)}")
    end
  end

  class CashOutService
    def initialize(client)
      @client = client
    end

    # POST /pix/payouts/dict: preview PIX key owner before payout.
    def dict_check(pix_key, key_type: nil)
      body = { "pix_key" => pix_key }
      body["key_type"] = key_type if key_type
      @client.request("POST", "/pix/payouts/dict", body: body)
    end

    # POST /cash-out/pix: send BRL via PIX (idempotency key required).
    def create(params, idempotency_key:)
      @client.request("POST", "/cash-out/pix", body: params, idempotency_key: idempotency_key)
    end

    # GET /cash-out/{id}.
    def retrieve(order_id)
      @client.request("GET", "/cash-out/#{Client.path_escape(order_id)}")
    end
  end

  class BalancesService
    def initialize(client)
      @client = client
    end

    # GET /account/balances.
    def list
      res = @client.request("GET", "/account/balances")
      bals = res["balances"]
      bals.is_a?(Array) ? bals : []
    end
  end

  class TransactionsService
    def initialize(client)
      @client = client
    end

    # GET /transactions with optional type/status/limit filters.
    def list(type: nil, status: nil, limit: nil)
      q = {}
      q["type"] = type if type && !type.empty?
      q["status"] = status if status && !status.empty?
      q["limit"] = limit.to_s if !limit.nil? && limit.to_i > 0

      path = "/transactions"
      path = "#{path}?#{URI.encode_www_form(q)}" unless q.empty?
      res = @client.request("GET", path)
      txs = res["transactions"]
      txs.is_a?(Array) ? txs : []
    end

    # GET /transactions/{id}.
    def retrieve(transaction_id)
      res = @client.request("GET", "/transactions/#{Client.path_escape(transaction_id)}")
      tx = res["transaction"]
      tx.is_a?(Hash) ? tx : res
    end
  end

  class WalletsService
    def initialize(client)
      @client = client
    end

    # GET /wallets: on-chain wallets (optional network filter).
    def list(network: nil)
      path = "/wallets"
      path = "#{path}?#{URI.encode_www_form('network' => network)}" if network && !network.empty?
      res = @client.request("GET", path)
      wallets = res["wallets"]
      wallets.is_a?(Array) ? wallets : []
    end

    # GET /wallets/{id}/balance.
    def retrieve_balance(wallet_id)
      @client.request("GET", "/wallets/#{Client.path_escape(wallet_id)}/balance")
    end
  end

  class WebhooksService
    def initialize(client)
      @client = client
    end

    # GET /webhooks.
    def list
      res = @client.request("GET", "/webhooks")
      subs = res["subscriptions"]
      subs.is_a?(Array) ? subs : []
    end

    # POST /webhooks: secret is returned once; store it.
    def create(params)
      res = @client.request("POST", "/webhooks", body: params)
      sub = res["subscription"].is_a?(Hash) ? res["subscription"].dup : {}
      sub["secret"] = res["secret"] if res.key?("secret")
      sub
    end

    # PATCH /webhooks/{id}.
    def update(webhook_id, params)
      res = @client.request(
        "PATCH",
        "/webhooks/#{Client.path_escape(webhook_id)}",
        body: params
      )
      sub = res["subscription"]
      sub.is_a?(Hash) ? sub : res
    end

    # DELETE /webhooks/{id}.
    def delete(webhook_id)
      @client.request("DELETE", "/webhooks/#{Client.path_escape(webhook_id)}")
      nil
    end
  end
end
