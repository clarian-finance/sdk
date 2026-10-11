# frozen_string_literal: true

module Clarian
  # Every non-2xx reply. `code` is the API's stable `error` string, e.g. "insufficient_balance".
  class ClarianError < StandardError
    attr_reader :status, :code, :meta

    def initialize(status, body)
      body = {} unless body.is_a?(Hash)
      @status = status
      @code = body["error"].is_a?(String) ? body["error"] : "unknown_error"
      @meta = body.reject { |k, _| %w[error message].include?(k) }
      super(body["message"].is_a?(String) ? body["message"] : @code)
    end
  end
end
