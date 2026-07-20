# frozen_string_literal: true

require_relative "lib/clarian/version"

Gem::Specification.new do |spec|
  spec.name          = "clarian"
  spec.version       = Clarian::VERSION
  spec.authors       = ["Clarian Finance"]
  spec.email         = ["contact@clarian.finance"]

  spec.summary       = "Official Ruby SDK for the Clarian Finance API"
  spec.description   = "PIX cash-in/cash-out, balances, wallets, transactions, and webhooks. Pure stdlib runtime."
  spec.homepage      = "https://github.com/clarian-finance/sdk"
  spec.license       = "MIT"
  spec.required_ruby_version = ">= 3.0.0"

  spec.metadata["source_code_uri"] = "https://github.com/clarian-finance/sdk"
  spec.metadata["homepage_uri"] = spec.homepage
  spec.metadata["rubygems_mfa_required"] = "true"

  spec.files = Dir.chdir(__dir__) do
    Dir["lib/**/*", "README.md", "LICENSE", "clarian.gemspec"].select { |f| File.file?(f) }
  end
  spec.require_paths = ["lib"]

  spec.add_development_dependency "minitest", "~> 5.0"
end
