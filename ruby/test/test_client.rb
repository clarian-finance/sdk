# frozen_string_literal: true

require "minitest/autorun"
require "json"
require "clarian"

class FakeTransport
  attr_reader :calls

  # replies: list of [status, body(Hash/String), headers] or an Exception to raise; the last one repeats.
  def initialize(*replies)
    @replies = replies
    @calls = []
  end

  def call(method, url, headers, body, _timeout)
    @calls << { method: method, url: url, headers: headers, body: body && JSON.parse(body) }
    r = @replies.length > 1 ? @replies.shift : @replies.first
    raise r if r.is_a?(Exception)

    status, payload, headers = r
    [status, payload.is_a?(String) ? payload : JSON.generate(payload), headers || {}]
  end
end

class TestClient < Minitest::Test
  KEY = "cl_test_sk_abc"
  WS = "ws_1"

  def client(*replies, **opts)
    @t = FakeTransport.new(*replies)
    @slept = []
    Clarian::Client.new(api_key: KEY, workspace_id: WS, transport: @t, sleeper: ->(s) { @slept << s }, **opts)
  end

  def test_env_from_key_prefix
    assert_equal "sandbox", client([200, {}]).environment
    c = Clarian::Client.new(api_key: "cl_live_sk_x", workspace_id: WS, transport: FakeTransport.new([200, {}]))
    assert_equal "live", c.environment
    assert_raises(ArgumentError) { Clarian::Client.new(api_key: "bad", workspace_id: WS) }
    assert_raises(ArgumentError) { Clarian::Client.new(api_key: KEY, workspace_id: "") }
  end

  def test_request_building_and_unwrap
    c = client([200, { "data" => [{ "id" => "p1" }] }], base_url: "http://x/")
    assert_equal [{ "id" => "p1" }], c.transactions.list(limit: 5, before: "a b")
    call = @t.calls.last
    assert_equal "GET", call[:method]
    assert_equal "http://x/v1/test/transactions?limit=5&before=a+b", call[:url]
    assert_equal "Bearer #{KEY}", call[:headers]["Authorization"]
    assert_equal WS, call[:headers]["X-Workspace-Id"]
    refute call[:headers].key?("Idempotency-Key")
    refute call[:headers].key?("Content-Type")
  end

  def test_live_default_base_url
    t = FakeTransport.new([200, { "data" => {} }])
    Clarian::Client.new(api_key: "cl_live_sk_x", workspace_id: WS, transport: t).ping
    assert_equal "https://api.clarian.finance/v1/live/ping", t.calls.last[:url]
  end

  def test_idempotent_create_headers_and_body
    c = client([200, { "data" => { "id" => "p" } }])
    c.pix.payins.create({ amount_minor: 10**30, currency: "BRL" }, "k-1")
    call = @t.calls.last
    assert_equal "POST", call[:method]
    assert call[:url].end_with?("/v1/test/pix/payins")
    assert_equal "k-1", call[:headers]["Idempotency-Key"]
    assert_equal "application/json", call[:headers]["Content-Type"]
    assert_equal 10**30, call[:body]["amount_minor"]
  end

  def test_subaccount_header_and_path_escape
    c = client([200, { "data" => {} }]).for_subaccount("sub_9")
    c.wallets.retrieve("a/b")
    assert_equal "sub_9", @t.calls.last[:headers]["X-Subaccount-Id"]
    assert @t.calls.last[:url].end_with?("/wallets/a%2Fb")
  end

  def test_floats_rejected
    c = client([200, {}])
    assert_raises(ArgumentError) { c.pix.payouts.fee({ amount_minor: 10.5 }) }
    assert_empty @t.calls
  end

  def test_idempotency_key_required
    c = client([200, {}])
    [-> { c.pix.payins.create({}, "") }, -> { c.pix.payouts.create({}, nil) }, -> { c.crypto.send({}, "") },
     -> { c.payment_links.charge("l", {}, "") }, -> { c.checkout.pay("s", {}, "") }].each do |f|
      assert_raises(ArgumentError, &f)
    end
    assert_empty @t.calls
  end

  def test_error_mapping
    c = client([402, { "error" => "insufficient_balance", "message" => "no money", "need" => "5" }])
    e = assert_raises(Clarian::ClarianError) { c.balances.list }
    assert_equal 402, e.status
    assert_equal "insufficient_balance", e.code
    assert_equal "no money", e.message
    assert_equal({ "need" => "5" }, e.meta)
    e = assert_raises(Clarian::ClarianError) { client([500, "oops"]).balances.list }
    assert_equal "invalid_json_response", e.code
  end

  def test_get_retried_honoring_retry_after
    c = client([503, {}, { "Retry-After" => "2" }], [429, {}], [200, { "data" => [] }])
    assert_equal [], c.balances.list
    assert_equal 3, @t.calls.size
    assert_equal 2.0, @slept.first
  end

  def test_retries_capped_at_max
    c = client([503, { "error" => "down" }], max_retries: 2)
    assert_raises(Clarian::ClarianError) { c.balances.list }
    assert_equal 3, @t.calls.size
  end

  def test_network_error_retried_for_get
    c = client(Errno::ECONNRESET.new, [200, { "data" => [] }])
    assert_equal [], c.balances.list
    assert_equal 2, @t.calls.size
  end

  def test_post_without_key_not_retried
    c = client([503, { "error" => "down" }])
    assert_raises(Clarian::ClarianError) { c.customers.create({ kind: "person" }) }
    assert_equal 1, @t.calls.size
  end

  def test_execute_not_retried
    c = client([503, { "error" => "down" }])
    assert_raises(Clarian::ClarianError) { c.conversions.execute("q1") }
    assert_equal 1, @t.calls.size
    refute @t.calls.last[:headers].key?("Idempotency-Key")
    assert_equal({ "quote_id" => "q1" }, @t.calls.last[:body])
  end

  def test_post_with_key_retried
    c = client([502, {}], [200, { "data" => { "id" => "p" } }])
    assert_equal({ "id" => "p" }, c.pix.payouts.create({ amount_minor: "1000" }, "k"))
    assert_equal 2, @t.calls.size
  end
end

class TestWebhook < Minitest::Test
  SECRET = "whsec_test"
  BODY = '{"id":"evt_1","type":"payment.completed","created_at":"2026-10-11T00:00:00Z","data":{"payment_id":"p1"}}'

  def headers(ts, sig = Clarian::Webhook.sign_payload(BODY, ts, SECRET))
    { "X-Clarian-Signature" => sig, "x-clarian-timestamp" => ts, "x-clarian-event" => "payment.completed" }
  end

  def test_valid
    ev = Clarian::Webhook.construct_event(BODY, headers(Time.now.utc.iso8601), SECRET)
    assert_equal "evt_1", ev["id"]
    assert_equal "p1", ev["data"]["payment_id"]
  end

  def test_rack_env_headers
    ts = Time.now.utc.iso8601
    env = { "HTTP_X_CLARIAN_SIGNATURE" => Clarian::Webhook.sign_payload(BODY, ts, SECRET), "HTTP_X_CLARIAN_TIMESTAMP" => ts }
    assert_equal "evt_1", Clarian::Webhook.construct_event(BODY, env, SECRET)["id"]
  end

  def test_bad_signature
    ts = Time.now.utc.iso8601
    assert_raises(ArgumentError) { Clarian::Webhook.construct_event(BODY, headers(ts, "0" * 64), SECRET) }
    refute Clarian::Webhook.verify_signature(BODY, Clarian::Webhook.sign_payload(BODY, ts, SECRET), ts, "other")
    refute Clarian::Webhook.verify_signature("#{BODY} ", Clarian::Webhook.sign_payload(BODY, ts, SECRET), ts, SECRET)
  end

  def test_stale
    ts = (Time.now - 301).utc.iso8601
    assert_raises(ArgumentError) { Clarian::Webhook.construct_event(BODY, headers(ts), SECRET) }
  end

  def test_unparseable_timestamp
    %w[not-a-date 1728600000].push("").each do |ts|
      refute Clarian::Webhook.verify_signature(BODY, Clarian::Webhook.sign_payload(BODY, ts, SECRET), ts, SECRET)
    end
  end
end
