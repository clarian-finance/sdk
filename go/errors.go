package clarian

import (
	"encoding/json"
	"fmt"
)

// APIError is returned for any non-2xx HTTP response from the Clarian API.
// Code is parsed from the JSON "error" field when the body is structured;
// otherwise Code is empty and Body holds the raw response text.
type APIError struct {
	Status int
	Code   string
	Body   string
}

// Error keeps the message log-safe: raw bodies may echo request fields
// (pix keys, documents), so only a truncated prefix is included. Full body
// stays available on the Body field for callers that need it.
func (e *APIError) Error() string {
	if e == nil {
		return "clarian: <nil>"
	}
	if e.Code != "" {
		return fmt.Sprintf("clarian: HTTP %d: %s", e.Status, e.Code)
	}
	if e.Body != "" {
		body := e.Body
		if len(body) > 256 {
			body = body[:256] + "…"
		}
		return fmt.Sprintf("clarian: HTTP %d: %s", e.Status, body)
	}
	return fmt.Sprintf("clarian: HTTP %d", e.Status)
}

func newAPIError(status int, body []byte) *APIError {
	err := &APIError{Status: status, Body: string(body)}
	if len(body) == 0 {
		return err
	}
	var parsed struct {
		Error string `json:"error"`
	}
	if json.Unmarshal(body, &parsed) == nil && parsed.Error != "" {
		err.Code = parsed.Error
	}
	return err
}
