# frozen_string_literal: true

require "json"
require "uri"

module ClarianTest
  RecordedRequest = Struct.new(:method, :url, :path, :headers, :body, keyword_init: true)

  # Records requests and returns canned responses via a handler.
  # Handler signature: (RecordedRequest) -> [status, body]
  # body may be Hash, String, or bytes-like.
  class FakeTransport
    attr_reader :calls

    def initialize(&handler)
      @handler = handler
      @calls = []
    end

    def call(method, url, headers, body, _timeout)
      uri = URI(url)
      path = uri.path
      path = "#{path}?#{uri.query}" if uri.query && !uri.query.empty?

      req = RecordedRequest.new(
        method: method,
        url: url,
        path: path,
        headers: headers.transform_keys(&:to_s),
        body: body
      )
      @calls << req

      status, payload = @handler.call(req)
      raw =
        case payload
        when nil then ""
        when String then payload
        when Hash, Array then JSON.generate(payload)
        else payload.to_s
        end
      [status, raw]
    end

    def last
      raise "no requests recorded" if @calls.empty?

      @calls.last
    end
  end

  # Fails the test if any HTTP call is attempted.
  class FailTransport
    def call(*)
      raise "unexpected HTTP call"
    end
  end
end
