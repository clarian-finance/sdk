<?php

declare(strict_types=1);

namespace Clarian;

/**
 * Default transport using the curl extension.
 */
final class CurlTransport implements HttpTransport
{
    public function request(
        string $method,
        string $url,
        array $headers,
        ?string $body,
        float $timeout,
    ): array {
        $ch = curl_init($url);
        if ($ch === false) {
            throw new \RuntimeException('clarian: failed to initialize curl');
        }

        $headerLines = [];
        foreach ($headers as $name => $value) {
            $headerLines[] = $name . ': ' . $value;
        }

        $opts = [
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER => $headerLines,
            CURLOPT_TIMEOUT => (int) ceil($timeout),
            CURLOPT_CONNECTTIMEOUT => (int) ceil($timeout),
            CURLOPT_HEADER => false,
        ];

        if ($body !== null) {
            $opts[CURLOPT_POSTFIELDS] = $body;
        }

        curl_setopt_array($ch, $opts);

        $response = curl_exec($ch);
        if ($response === false) {
            $err = curl_error($ch);
            curl_close($ch);
            throw new \RuntimeException('clarian: request failed: ' . $err);
        }

        $status = (int) curl_getinfo($ch, CURLINFO_HTTP_CODE);
        curl_close($ch);

        return [$status, (string) $response];
    }
}
