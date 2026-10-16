# frozen_string_literal: true

module Clarian
  # Entry point. The key prefix picks the environment: cl_test_sk_ -> /v1/test, cl_live_sk_ -> /v1/live.
  class Client
    attr_reader :environment, :balances, :wallets, :transactions, :pix, :conversions, :trading, :crypto, :customers, :kyc,
                :whitelist, :bank_accounts, :webhooks, :payment_links, :checkout, :subaccounts, :api_keys,
                :settings, :notifications, :team, :workflows, :approvals, :treasury

    # Options: base_url, timeout (seconds, default 30), subaccount_id, max_retries (default 2),
    # transport and sleeper (tests, proxies).
    def initialize(api_key:, workspace_id:, http: nil, **opts)
      raise ArgumentError, "api_key is required" if api_key.nil? || api_key.empty?
      raise ArgumentError, "workspace_id is required" if workspace_id.nil? || workspace_id.empty?

      @http = http || Http.new(api_key: api_key, workspace_id: workspace_id, **opts)
      @environment = @http.environment
      @balances = Balances.new(@http)
      @wallets = Wallets.new(@http)
      @transactions = Transactions.new(@http)
      @pix = Pix.new(@http)
      @conversions = Conversions.new(@http)
      @trading = Trading.new(@http)
      @crypto = Crypto.new(@http)
      @customers = Customers.new(@http)
      @kyc = Kyc.new(@http)
      @whitelist = Whitelist.new(@http)
      @bank_accounts = BankAccounts.new(@http)
      @webhooks = Webhooks.new(@http)
      @payment_links = PaymentLinks.new(@http)
      @checkout = Checkout.new(@http)
      @subaccounts = SubAccounts.new(@http)
      @api_keys = ApiKeys.new(@http)
      @settings = Settings.new(@http)
      @notifications = Notifications.new(@http)
      @team = Team.new(@http)
      @workflows = Workflows.new(@http)
      @approvals = Approvals.new(@http)
      @treasury = Treasury.new(@http)
    end

    # Never prints the API key.
    def inspect = "#<Clarian::Client #{@http.inspect}>"
    alias to_s inspect

    # Checks the key and workspace.
    def ping = @http.request("GET", "ping")

    # A client that acts on one child workspace (sends X-Subaccount-Id).
    def for_subaccount(subaccount_id)
      raise ArgumentError, "subaccount_id is required" if subaccount_id.to_s.strip.empty?

      Client.new(api_key: @http.api_key, workspace_id: @http.workspace_id, http: @http.with_subaccount(subaccount_id))
    end
  end
end
