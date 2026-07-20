# frozen_string_literal: true

require "json"
require "minitest/autorun"
require "clarian"
require_relative "fake_transport"

class TestSandboxGuard < Minitest::Test
  def test_live_key_rejected_before_network
    c = Clarian::Client.new(
      api_key: "cl_live_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: ClarianTest::FailTransport.new
    )
    err = assert_raises(ArgumentError) do
      c.sandbox.simulate_cash_in("ord_1", "completed")
    end
    assert_includes err.message, "cl_test_sk_"

    assert_raises(ArgumentError) { c.sandbox.simulate_cash_out("out_1", "completed") }
    assert_raises(ArgumentError) do
      c.sandbox.send_webhook_event("wh1", Clarian::EVENT_PIX_PAYIN_COMPLETED)
    end
    assert_raises(ArgumentError) { c.sandbox.resend_webhook_delivery("del_1") }
  end

  def test_invalid_status_no_http
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: ClarianTest::FailTransport.new
    )
    err = assert_raises(ArgumentError) { c.sandbox.simulate_cash_in("ord_1", "bogus") }
    assert_includes err.message, "invalid cash-in"

    err = assert_raises(ArgumentError) { c.sandbox.simulate_cash_out("out_1", "expired") }
    assert_includes err.message, "invalid cash-out"
  end
end

class TestSandboxHappyPaths < Minitest::Test
  def test_simulate_cash_in
    mt = ClarianTest::FakeTransport.new do |_req|
      [
        200,
        {
          "ok" => true,
          "environment" => "sandbox",
          "order" => { "id" => "ord_1", "status" => "completed", "amount" => 19.50 }
        }
      ]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws-uuid",
      base_url: "https://mock",
      transport: mt
    )
    res = c.sandbox.simulate_cash_in("ord_1", "completed")
    assert_equal "ord_1", res["order"]["id"]
    assert_equal "POST", mt.last.method
    assert_equal "/pix/payins/ord_1/simulate", mt.last.path
    assert_equal "Bearer cl_test_sk_x", mt.last.headers["Authorization"]
    body = JSON.parse(mt.last.body)
    assert_equal "completed", body["status"]
  end

  def test_simulate_cash_in_default_status
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "order" => { "id" => "ord_2", "status" => "completed" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    c.sandbox.simulate_cash_in("ord_2")
    body = JSON.parse(mt.last.body)
    assert_equal "completed", body["status"]
  end

  def test_simulate_cash_out
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "order" => { "id" => "out_1", "status" => "failed" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    res = c.sandbox.simulate_cash_out("out_1", "failed")
    assert_equal "failed", res["order"]["status"]
    assert_equal "/pix/payouts/out_1/simulate", mt.last.path
  end

  def test_send_webhook_event
    mt = ClarianTest::FakeTransport.new do |req|
      assert_equal "/webhooks/wh1/test", req.path
      [
        202,
        {
          "ok" => true,
          "environment" => "sandbox",
          "event_type" => "pix_payin.completed",
          "enqueued" => { "enqueued" => 1, "delivery_id" => "del_99" }
        }
      ]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    res = c.sandbox.send_webhook_event("wh1", Clarian::EVENT_PIX_PAYIN_COMPLETED)
    assert_equal Clarian::EVENT_PIX_PAYIN_COMPLETED, res["event_type"]
    assert_equal 1, res["enqueued"]
    assert_equal "del_99", res["delivery_id"]
    body = JSON.parse(mt.last.body)
    assert_equal "pix_payin.completed", body["event_type"]
  end

  def test_resend_webhook_delivery
    mt = ClarianTest::FakeTransport.new do |_req|
      [202, { "ok" => true, "delivery_id" => "del_new" }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    new_id = c.sandbox.resend_webhook_delivery("del_old")
    assert_equal "del_new", new_id
    assert_equal "/webhooks/deliveries/del_old/resend", mt.last.path
    assert_equal "POST", mt.last.method
  end

  def test_magic_pix_key_constants
    assert_equal "fail@sandbox.clarian", Clarian::SANDBOX_FAIL_PIX_KEY
    assert_equal "pending@sandbox.clarian", Clarian::SANDBOX_PENDING_PIX_KEY
  end

  def test_path_escape
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "order" => { "id" => "x", "status" => "completed" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    c.sandbox.simulate_cash_in("a/b", "completed")
    assert_equal "/pix/payins/a%2Fb/simulate", mt.last.path
  end
end
