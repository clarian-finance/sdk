# frozen_string_literal: true

module Clarian
  # Magic PIX keys honored by the sandbox payout rail.
  SANDBOX_FAIL_PIX_KEY = "fail@sandbox.clarian"
  SANDBOX_PENDING_PIX_KEY = "pending@sandbox.clarian"

  # Sample webhook event types for sandbox testing (exact gateway strings).
  EVENT_PIX_PAYIN_CREATED = "pix_payin.created"
  EVENT_PIX_PAYIN_COMPLETED = "pix_payin.completed"
  EVENT_PIX_PAYIN_EXPIRED = "pix_payin.expired"
  EVENT_PIX_PAYOUT_CREATED = "pix_payout.created"
  EVENT_PIX_PAYOUT_COMPLETED = "pix_payout.completed"
  EVENT_PIX_PAYOUT_FAILED = "pix_payout.failed"
  EVENT_CHECKOUT_PAID = "checkout.paid"

  CASH_IN_SIMULATE_STATUSES = %w[completed expired failed].freeze
  CASH_OUT_SIMULATE_STATUSES = %w[completed failed].freeze

  # Sandbox-only test helpers. Refuse live keys before any HTTP call.
  class SandboxService
    def initialize(client)
      @client = client
    end

    # POST /pix/payins/{id}/simulate; default status is completed.
    def simulate_cash_in(order_id, status = nil)
      require_test_key!
      resolved = status.nil? || status.to_s.empty? ? "completed" : status.to_s
      unless CASH_IN_SIMULATE_STATUSES.include?(resolved)
        raise ArgumentError, "invalid cash-in simulate status #{resolved.inspect}"
      end

      path = "/pix/payins/#{Client.path_escape(order_id)}/simulate"
      @client.request("POST", path, body: { "status" => resolved })
    end

    # POST /pix/payouts/{id}/simulate; default status is completed.
    def simulate_cash_out(order_id, status = nil)
      require_test_key!
      resolved = status.nil? || status.to_s.empty? ? "completed" : status.to_s
      unless CASH_OUT_SIMULATE_STATUSES.include?(resolved)
        raise ArgumentError, "invalid cash-out simulate status #{resolved.inspect}"
      end

      path = "/pix/payouts/#{Client.path_escape(order_id)}/simulate"
      @client.request("POST", path, body: { "status" => resolved })
    end

    # POST /webhooks/{id}/test: enqueue a sample delivery.
    def send_webhook_event(subscription_id, event_type)
      require_test_key!
      path = "/webhooks/#{Client.path_escape(subscription_id)}/test"
      res = @client.request("POST", path, body: { "event_type" => event_type })
      enqueued = res["enqueued"].is_a?(Hash) ? res["enqueued"] : {}
      delivery_id = enqueued["delivery_id"]
      {
        "ok" => res["ok"],
        "environment" => res["environment"],
        "event_type" => res["event_type"],
        "enqueued" => enqueued.fetch("enqueued", 0),
        "delivery_id" => delivery_id.nil? ? "" : delivery_id
      }
    end

    # POST /webhooks/deliveries/{id}/resend; returns new delivery id.
    def resend_webhook_delivery(delivery_id)
      require_test_key!
      path = "/webhooks/deliveries/#{Client.path_escape(delivery_id)}/resend"
      res = @client.request("POST", path)
      (res["delivery_id"] || "").to_s
    end

    private

    def require_test_key!
      return if @client.api_key.start_with?("cl_test_sk_")

      raise ArgumentError, "sandbox helpers require a cl_test_sk_ key"
    end
  end
end
