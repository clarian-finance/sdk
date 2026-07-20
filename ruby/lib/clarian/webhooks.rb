# frozen_string_literal: true

require "openssl"
require "time"

module Clarian
  # Webhook signature helpers (HMAC-SHA256 hex over timestamp.body).
  module Webhooks
    HEADER_SIGNATURE = "X-Clarian-Signature"
    HEADER_TIMESTAMP = "X-Clarian-Timestamp"
    HEADER_EVENT = "X-Clarian-Event"
    HEADER_DELIVERY_ID = "X-Clarian-Delivery-Id"
    HEADER_IDEMPOTENCY_KEY = "X-Clarian-Idempotency-Key"
    HEADER_ATTEMPT = "X-Clarian-Attempt"

    DEFAULT_TOLERANCE_SECONDS = 300

    module_function

    # Return hex HMAC-SHA256 of "#{timestamp}.#{body}" using secret.
    def sign_payload(secret:, timestamp:, payload:)
      body = payload.is_a?(String) ? payload : payload.to_s
      OpenSSL::HMAC.hexdigest("SHA256", secret.to_s, "#{timestamp}.#{body}")
    end

    # Verify HMAC-SHA256 signature and timestamp freshness.
    # Returns false on missing inputs, bad signature, or stale/future timestamp.
    def verify_signature(payload:, timestamp:, signature:, secret:, tolerance_seconds: DEFAULT_TOLERANCE_SECONDS)
      return false if secret.to_s.empty? || signature.to_s.empty? || timestamp.to_s.empty?

      begin
        event_ts = parse_timestamp(timestamp)
      rescue ArgumentError
        return false
      end

      age = (Time.now.to_f - event_ts).abs
      return false if age > tolerance_seconds

      body = payload.is_a?(String) ? payload : payload.to_s
      expected = sign_payload(secret: secret, timestamp: timestamp, payload: body)
      return false if expected.bytesize != signature.bytesize

      OpenSSL.secure_compare(expected, signature)
    end

    # Read Clarian delivery headers (case-insensitive). Missing values become empty strings.
    # Returns a Hash with keys: signature, timestamp, event, delivery_id, idempotency_key, attempt.
    def extract_headers(headers)
      return empty_headers if headers.nil?

      lower = {}
      headers.each do |key, value|
        next if key.nil?

        lower[key.to_s.downcase] = first_value(value)
      end

      {
        signature: lower[HEADER_SIGNATURE.downcase] || "",
        timestamp: lower[HEADER_TIMESTAMP.downcase] || "",
        event: lower[HEADER_EVENT.downcase] || "",
        delivery_id: lower[HEADER_DELIVERY_ID.downcase] || "",
        idempotency_key: lower[HEADER_IDEMPOTENCY_KEY.downcase] || "",
        attempt: lower[HEADER_ATTEMPT.downcase] || ""
      }
    end

    def empty_headers
      {
        signature: "",
        timestamp: "",
        event: "",
        delivery_id: "",
        idempotency_key: "",
        attempt: ""
      }
    end
    private_class_method :empty_headers

    def first_value(value)
      return "" if value.nil?
      return value.first.to_s if value.is_a?(Array)
      value.to_s
    end
    private_class_method :first_value

    def parse_timestamp(timestamp)
      # Unix epoch (seconds as string or number-like)
      if timestamp.match?(/\A-?\d+(\.\d+)?\z/)
        return timestamp.to_f
      end

      Time.iso8601(timestamp).to_f
    rescue ArgumentError
      raise ArgumentError, "invalid webhook timestamp: #{timestamp.inspect}"
    end
    private_class_method :parse_timestamp
  end
end
