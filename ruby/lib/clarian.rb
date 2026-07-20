# frozen_string_literal: true

require_relative "clarian/version"
require_relative "clarian/error"
require_relative "clarian/webhooks"
require_relative "clarian/client"
require_relative "clarian/sandbox"

module Clarian
  # Convenience re-exports for Clarian::Webhooks helpers.
  def self.sign_payload(**kwargs)
    Webhooks.sign_payload(**kwargs)
  end

  def self.verify_signature(**kwargs)
    Webhooks.verify_signature(**kwargs)
  end

  def self.extract_headers(headers)
    Webhooks.extract_headers(headers)
  end
end
