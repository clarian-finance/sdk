# frozen_string_literal: true

require "json"
require "openssl"
require "time"

module Clarian
  # Signing helpers for webhook deliveries.
  module Webhook
    DEFAULT_TOLERANCE = 300 # seconds

    module_function

    # Signs "<timestamp>.<body>" exactly as Clarian does. For tests of your own handler.
    def sign_payload(body, timestamp, secret)
      OpenSSL::HMAC.hexdigest("SHA256", secret, "#{timestamp}.#{body}")
    end

    def verify_signature(body, signature, timestamp, secret, tolerance: DEFAULT_TOLERANCE)
      # An empty secret makes the HMAC forgeable by anyone: refuse it outright.
      raise ArgumentError, "webhook secret is required" if secret.nil? || secret.to_s.empty?

      # An unparseable timestamp fails closed.
      sent_at = begin
        Time.iso8601(timestamp.to_s)
      rescue ArgumentError
        return false
      end
      return false if (Time.now - sent_at).abs > tolerance

      expected = sign_payload(body, timestamp, secret)
      sig = signature.to_s
      expected.bytesize == sig.bytesize && OpenSSL.fixed_length_secure_compare(expected, sig)
    end

    # Reads the x-clarian-* headers from a Hash (any key case, String or Array values) or a Rack env.
    def extract_headers(headers)
      get = lambda do |name|
        rack = "HTTP_#{name.upcase.tr('-', '_')}"
        hit = headers.find { |k, _| k.to_s.downcase == name || k.to_s == rack }&.last
        (hit.is_a?(Array) ? hit.first : hit).to_s
      end
      { signature: get.("x-clarian-signature"), timestamp: get.("x-clarian-timestamp"), event: get.("x-clarian-event") }
    end

    # Verifies a delivery and returns its parsed body {"id","type","created_at","data"}.
    # raw_body must be the exact bytes received. Raises on a bad signature or stale timestamp.
    # Dedupe on event["id"]: a delivery can arrive more than once.
    def construct_event(raw_body, headers, secret, tolerance: DEFAULT_TOLERANCE)
      h = extract_headers(headers)
      unless verify_signature(raw_body, h[:signature], h[:timestamp], secret, tolerance: tolerance)
        raise ArgumentError, "Invalid webhook signature or timestamp outside tolerance"
      end

      JSON.parse(raw_body)
    end
  end
end
