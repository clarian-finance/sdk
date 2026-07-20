# frozen_string_literal: true

require "json"
require "minitest/autorun"
require "clarian"
require_relative "fake_transport"

class TestEnvDetection < Minitest::Test
  def test_test_prefix
    c = Clarian::Client.new(api_key: "cl_test_sk_abc", workspace_id: "ws")
    assert_equal Clarian::BASE_URL_TEST, c.base_url
  end

  def test_live_prefix
    c = Clarian::Client.new(api_key: "cl_live_sk_abc", workspace_id: "ws")
    assert_equal Clarian::BASE_URL_LIVE, c.base_url
  end

  def test_unknown_defaults_live
    c = Clarian::Client.new(api_key: "other_key", workspace_id: "ws")
    assert_equal Clarian::BASE_URL_LIVE, c.base_url
  end

  def test_base_url_override_strips_trailing_slash
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "http://localhost:9999/v1/"
    )
    assert_equal "http://localhost:9999/v1", c.base_url
  end
end

class TestAuthHeaders < Minitest::Test
  def test_workspace_and_auth_on_every_request
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "environment" => "sandbox", "master_account_id" => "m1" }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_key",
      workspace_id: "ws-uuid",
      base_url: "https://mock",
      transport: mt
    )
    res = c.ping
    assert_equal true, res["ok"]
    assert_equal "GET", mt.last.method
    assert_equal "/ping", mt.last.path
    assert_equal "Bearer cl_test_sk_key", mt.last.headers["Authorization"]
    assert_equal "ws-uuid", mt.last.headers["X-Workspace-Id"]
  end
end

class TestIdempotencyHeader < Minitest::Test
  def test_cash_in_create_sends_idempotency_key
    mt = ClarianTest::FakeTransport.new do |_req|
      [
        201,
        {
          "ok" => true,
          "environment" => "sandbox",
          "order" => {
            "id" => "ord_1",
            "status" => "pending",
            "amount" => 19.50,
            "currency" => "BRL",
            "pix" => { "copy_paste" => "brcode" }
          }
        }
      ]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws-uuid",
      base_url: "https://mock",
      transport: mt
    )
    res = c.cash_in.create(
      {
        "amount" => 19.50,
        "payer" => { "name" => "Ana", "document_number" => "52998224725" }
      },
      idempotency_key: "pay-ext-1"
    )
    assert_equal "ord_1", res["order"]["id"]
    assert_equal "POST", mt.last.method
    assert_equal "/cash-in/pix", mt.last.path
    assert_equal "pay-ext-1", mt.last.headers["Idempotency-Key"]
    assert_equal "ws-uuid", mt.last.headers["X-Workspace-Id"]
    body = JSON.parse(mt.last.body)
    assert_equal 19.50, body["amount"]
    assert_equal "Ana", body["payer"]["name"]
  end

  def test_cash_out_create_sends_idempotency_key
    mt = ClarianTest::FakeTransport.new do |_req|
      [201, { "ok" => true, "order" => { "id" => "out_9", "status" => "processing" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    c.cash_out.create(
      { "amount" => 10.00, "pix_key" => "abc", "pix_key_type" => "EVP" },
      idempotency_key: "repasse-1"
    )
    assert_equal "repasse-1", mt.last.headers["Idempotency-Key"]
    assert_equal "/cash-out/pix", mt.last.path
  end

  def test_get_does_not_send_idempotency_key
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "order" => { "id" => "x", "status" => "pending" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    c.cash_in.retrieve("x")
    refute mt.last.headers.key?("Idempotency-Key")
  end
end

class TestResourceHappyPaths < Minitest::Test
  def test_cash_in_retrieve
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "order" => { "id" => "ord_paid", "status" => "completed" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_live_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    res = c.cash_in.retrieve("ord_paid")
    assert_equal "completed", res["order"]["status"]
    assert_equal "/cash-in/ord_paid", mt.last.path
  end

  def test_cash_out_retrieve
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "order" => { "id" => "out_1", "status" => "completed" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    res = c.cash_out.retrieve("out_1")
    assert_equal "out_1", res["order"]["id"]
    assert_equal "/cash-out/out_1", mt.last.path
  end

  def test_cash_out_dict_check
    mt = ClarianTest::FakeTransport.new do |_req|
      [200, { "ok" => true, "dict" => { "name" => "Maria", "keyType" => "EMAIL" } }]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    res = c.cash_out.dict_check("maria@example.com", key_type: "EMAIL")
    assert_equal "Maria", res["dict"]["name"]
    assert_equal "POST", mt.last.method
    assert_equal "/pix/payouts/dict", mt.last.path
    body = JSON.parse(mt.last.body)
    assert_equal "maria@example.com", body["pix_key"]
    assert_equal "EMAIL", body["key_type"]
  end

  def test_balances_list
    mt = ClarianTest::FakeTransport.new do |_req|
      [
        200,
        {
          "ok" => true,
          "balances" => [
            { "currency" => "BRL", "available" => 100.50, "pending" => 0, "locked" => 0 }
          ]
        }
      ]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    bals = c.balances.list
    assert_equal 1, bals.length
    assert_equal "BRL", bals[0]["currency"]
    assert_equal "/account/balances", mt.last.path
  end

  def test_transactions_list_and_retrieve
    mt = ClarianTest::FakeTransport.new do |req|
      if req.path.start_with?("/transactions?") || req.path == "/transactions"
        assert_includes req.path, "type=pix_in"
        assert_includes req.path, "limit=20"
        [
          200,
          {
            "transactions" => [
              {
                "id" => "tx1",
                "type" => "pix_in",
                "status" => "completed",
                "amount" => 10,
                "fee" => 0,
                "currency" => "BRL"
              }
            ]
          }
        ]
      elsif req.path == "/transactions/tx-99"
        [
          200,
          {
            "transaction" => {
              "id" => "tx-99",
              "type" => "pix_in",
              "status" => "completed",
              "amount" => 10,
              "fee" => 0.1,
              "currency" => "BRL"
            }
          }
        ]
      else
        flunk "unexpected path #{req.path}"
      end
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    lst = c.transactions.list(type: "pix_in", limit: 20)
    assert_equal "tx1", lst[0]["id"]
    tx = c.transactions.retrieve("tx-99")
    assert_equal "tx-99", tx["id"]
  end

  def test_wallets_list_and_balance
    mt = ClarianTest::FakeTransport.new do |req|
      if req.path == "/wallets" || req.path.start_with?("/wallets?")
        [
          200,
          {
            "ok" => true,
            "wallets" => [
              { "id" => "w1", "network" => "polygon", "address" => "0xabc" }
            ]
          }
        ]
      elsif req.path == "/wallets/w1/balance"
        [
          200,
          {
            "wallet_id" => "w1",
            "network" => "polygon",
            "address" => "0xabc",
            "balances" => [{ "currency" => "USDT", "amount" => "1.5" }]
          }
        ]
      else
        flunk "unexpected path #{req.path}"
      end
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    wallets = c.wallets.list
    assert_equal "w1", wallets[0]["id"]
    bal = c.wallets.retrieve_balance("w1")
    assert_equal "w1", bal["wallet_id"]
    assert_equal "USDT", bal["balances"][0]["currency"]
  end

  def test_webhooks_crud
    mt = ClarianTest::FakeTransport.new do |req|
      if req.method == "POST" && req.path == "/webhooks"
        [
          201,
          {
            "ok" => true,
            "subscription" => {
              "id" => "wh1",
              "url" => "https://ex.com",
              "events" => ["pix_payin.completed"],
              "is_active" => true
            },
            "secret" => "whsec_abc"
          }
        ]
      elsif req.method == "GET" && req.path == "/webhooks"
        [
          200,
          {
            "subscriptions" => [
              {
                "id" => "wh1",
                "url" => "https://ex.com",
                "events" => ["pix_payin.completed"],
                "is_active" => true
              }
            ]
          }
        ]
      elsif req.method == "PATCH" && req.path == "/webhooks/wh1"
        [
          200,
          {
            "subscription" => {
              "id" => "wh1",
              "url" => "https://ex.com/v2",
              "events" => ["pix_payin.completed"],
              "is_active" => false
            }
          }
        ]
      elsif req.method == "DELETE" && req.path == "/webhooks/wh1"
        [200, { "ok" => true }]
      else
        flunk "unexpected #{req.method} #{req.path}"
      end
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    created = c.webhooks.create(
      { "url" => "https://ex.com", "events" => ["pix_payin.completed"] }
    )
    assert_equal "whsec_abc", created["secret"]
    assert_equal "wh1", created["id"]
    subs = c.webhooks.list
    assert_equal 1, subs.length
    upd = c.webhooks.update(
      "wh1",
      {
        "url" => "https://ex.com/v2",
        "events" => ["pix_payin.completed"],
        "is_active" => false
      }
    )
    assert_equal false, upd["is_active"]
    c.webhooks.delete("wh1")
    assert_equal "DELETE", mt.calls.last.method
  end
end

class TestClarianError < Minitest::Test
  def test_json_code_and_meta
    mt = ClarianTest::FakeTransport.new do |_req|
      [
        402,
        {
          "error" => "insufficient_balance",
          "detail" => "saldo insuficiente",
          "available" => 10,
          "requested" => 100
        }
      ]
    end
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    err = assert_raises(Clarian::Error) do
      c.cash_out.create({ "amount" => 100 }, idempotency_key: "idem")
    end
    assert_equal 402, err.status
    assert_equal "insufficient_balance", err.code
    assert_equal "saldo insuficiente", err.message
    assert_equal 10, err.meta["available"]
    assert_includes err.to_s, "insufficient_balance"
  end

  def test_body_truncation_in_message
    long_body = "x" * 600
    mt = ClarianTest::FakeTransport.new { |_req| [429, long_body] }
    c = Clarian::Client.new(
      api_key: "cl_test_sk_x",
      workspace_id: "ws",
      base_url: "https://mock",
      transport: mt
    )
    err = assert_raises(Clarian::Error) { c.ping }
    assert_equal 429, err.status
    assert_equal "", err.code
    assert_equal 500, err.message.length
    assert_operator err.to_s.length, :<=, 500 + "HTTP 429: ".length
    refute_includes err.to_s, "x" * 600
    assert_equal long_body, err.body
  end
end
