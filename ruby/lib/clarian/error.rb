# frozen_string_literal: true

require "json"

module Clarian
  # Raised for any non-2xx HTTP response from the Clarian API.
  class Error < StandardError
    attr_reader :status, :code, :message, :meta, :body

    def initialize(status, body = nil)
      raw =
        case body
        when nil then ""
        when String then body
        else body.to_s
        end

      code = ""
      detail = ""
      meta = {}

      if !raw.empty?
        begin
          parsed = JSON.parse(raw)
          if parsed.is_a?(Hash)
            code = parsed["error"].to_s if parsed.key?("error") && !parsed["error"].nil?
            detail = parsed["detail"].to_s if parsed.key?("detail") && !parsed["detail"].nil?
            meta = parsed.reject { |k, _| %w[error detail hint].include?(k) }
            meta = meta.merge("hint" => parsed["hint"]) if parsed.key?("hint") && !parsed["hint"].nil?
          end
        rescue JSON::ParserError
          # leave code/detail/meta empty
        end
      end

      truncated = raw.length <= 500 ? raw : raw[0, 500]
      message =
        if !detail.empty?
          detail
        elsif !code.empty?
          code
        else
          truncated
        end

      exc_msg =
        if !code.empty?
          "HTTP #{status}: #{code}"
        elsif !truncated.empty?
          "HTTP #{status}: #{truncated}"
        else
          "HTTP #{status}"
        end

      super(exc_msg)
      @status = status
      @code = code
      @message = message
      @meta = meta
      @body = raw
    end
  end
end
