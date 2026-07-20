# frozen_string_literal: true

require "minitest/autorun"
require "clarian"
require "time"

class TestWebhooks < Minitest::Test
  def setup
    @secret = "whsec_test_secret"
    @body = '{"id":"evt_1","type":"pix_payin.completed",' \
            '"created_at":"2026-01-01T00:00:00Z","environment":"sandbox",' \
            '"data":{"transaction_id":"ord_abc","status":"completed",' \
            '"amount":19.50,"fee":0.37,"currency":"BRL"}}'
    @ts = Time.now.utc.strftime("%Y-%m-%dT%H:%M:%S.%6NZ")
  end

  def test_sign_verify_roundtrip
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: @ts, payload: @body)
    assert Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: @ts,
      signature: sig,
      secret: @secret
    )
  end

  def test_module_level_helpers
    sig = Clarian.sign_payload(secret: @secret, timestamp: @ts, payload: @body)
    assert Clarian.verify_signature(
      payload: @body,
      timestamp: @ts,
      signature: sig,
      secret: @secret
    )
  end

  def test_tampered_signature_rejected
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: @ts, payload: @body)
    tampered = @body.sub("ord_abc", "ord_evil")
    refute Clarian::Webhooks.verify_signature(
      payload: tampered,
      timestamp: @ts,
      signature: sig,
      secret: @secret
    )
  end

  def test_wrong_secret_rejected
    sig = Clarian::Webhooks.sign_payload(secret: "whsec_wrong", timestamp: @ts, payload: @body)
    refute Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: @ts,
      signature: sig,
      secret: @secret
    )
  end

  def test_stale_timestamp_rejected
    stale = (Time.now.utc - 360).strftime("%Y-%m-%dT%H:%M:%S.%6NZ")
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: stale, payload: @body)
    refute Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: stale,
      signature: sig,
      secret: @secret
    )
  end

  def test_future_timestamp_beyond_tolerance_rejected
    future = (Time.now.utc + 360).strftime("%Y-%m-%dT%H:%M:%S.%6NZ")
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: future, payload: @body)
    refute Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: future,
      signature: sig,
      secret: @secret
    )
  end

  def test_custom_tolerance
    old = (Time.now.utc - 10).strftime("%Y-%m-%dT%H:%M:%S.%6NZ")
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: old, payload: @body)
    refute Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: old,
      signature: sig,
      secret: @secret,
      tolerance_seconds: 5
    )
    assert Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: old,
      signature: sig,
      secret: @secret,
      tolerance_seconds: 60
    )
  end

  def test_missing_inputs_rejected
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: @ts, payload: @body)
    refute Clarian::Webhooks.verify_signature(
      payload: @body, timestamp: @ts, signature: sig, secret: ""
    )
    refute Clarian::Webhooks.verify_signature(
      payload: @body, timestamp: "", signature: sig, secret: @secret
    )
    refute Clarian::Webhooks.verify_signature(
      payload: @body, timestamp: @ts, signature: "", secret: @secret
    )
  end

  def test_extract_headers
    headers = {
      Clarian::Webhooks::HEADER_TIMESTAMP => @ts,
      Clarian::Webhooks::HEADER_SIGNATURE => "abc123",
      "X-Clarian-Event" => "pix_payin.completed"
    }
    extracted = Clarian::Webhooks.extract_headers(headers)
    assert_equal @ts, extracted[:timestamp]
    assert_equal "abc123", extracted[:signature]
    assert_equal "pix_payin.completed", extracted[:event]
  end

  def test_extract_headers_case_insensitive
    headers = {
      "x-clarian-timestamp" => "t1",
      "x-clarian-signature" => "s1"
    }
    extracted = Clarian::Webhooks.extract_headers(headers)
    assert_equal "t1", extracted[:timestamp]
    assert_equal "s1", extracted[:signature]
  end

  def test_extract_headers_array_values
    headers = {
      "X-Clarian-Timestamp" => ["t2"],
      "X-Clarian-Signature" => ["s2"]
    }
    extracted = Clarian::Webhooks.extract_headers(headers)
    assert_equal "t2", extracted[:timestamp]
    assert_equal "s2", extracted[:signature]
  end

  def test_unix_timestamp_accepted
    now = Time.now.to_i.to_s
    sig = Clarian::Webhooks.sign_payload(secret: @secret, timestamp: now, payload: @body)
    assert Clarian::Webhooks.verify_signature(
      payload: @body,
      timestamp: now,
      signature: sig,
      secret: @secret
    )
  end
end
