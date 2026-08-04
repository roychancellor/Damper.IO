$hostName = "localhost"
$port = "15672"
$user = "guest"
$pass = "guest"

# Build Auth Header
$pair = "$($user):$($pass)"
$bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [System.Convert]::ToBase64String($bytes)
$headers = @{ Authorization = "Basic $base64" }
$baseUri = "http://$($hostName):$($port)/api"

# 1. CLEANUP: Delete existing exchanges/queues
Write-Host "Cleaning up existing infrastructure..." -ForegroundColor Yellow
try { Invoke-RestMethod -Uri "$baseUri/exchanges/%2f/damper.message.exchange" -Method Delete -Headers $headers -ErrorAction SilentlyContinue } catch { }
try { Invoke-RestMethod -Uri "$baseUri/exchanges/%2f/damper.parking.exchange" -Method Delete -Headers $headers -ErrorAction SilentlyContinue } catch { }
for ($i = 0; $i -lt 16; $i++) {
    $shardName = "damper.message.queue.shard_$($i.ToString('D2'))"
    try { Invoke-RestMethod -Uri "$baseUri/queues/%2f/$shardName" -Method Delete -Headers $headers -ErrorAction SilentlyContinue } catch { }
}
try { Invoke-RestMethod -Uri "$baseUri/queues/%2f/damper.message.queue.dead_letter" -Method Delete -Headers $headers -ErrorAction SilentlyContinue } catch { }
try { Invoke-RestMethod -Uri "$baseUri/queues/%2f/damper.message.queue.parking" -Method Delete -Headers $headers -ErrorAction SilentlyContinue } catch { }

# 2. PROVISION: Dead Letter Exchange & Queue
Invoke-RestMethod -Uri "$baseUri/exchanges/%2f/damper.dlx" -Method Put -Headers $headers -Body (@{ type = "direct"; durable = $true } | ConvertTo-Json) -ContentType "application/json"
Invoke-RestMethod -Uri "$baseUri/queues/%2f/damper.message.queue.dead_letter" -Method Put -Headers $headers -Body (@{ durable = $true; arguments = @{ "x-queue-type" = "quorum" } } | ConvertTo-Json) -ContentType "application/json"
Invoke-RestMethod -Uri "$baseUri/bindings/%2f/e/damper.dlx/q/damper.message.queue.dead_letter" -Method Post -Headers $headers -Body (@{ routing_key = "dead-letter" } | ConvertTo-Json) -ContentType "application/json"

# 3. PROVISION: Main Consistent Hash Exchange
Invoke-RestMethod -Uri "$baseUri/exchanges/%2f/damper.message.exchange" -Method Put -Headers $headers -Body (@{
    type = "x-consistent-hash"; durable = $true
} | ConvertTo-Json) -ContentType "application/json"

# 4. PROVISION & BIND: 16 Shard Queues
for ($i = 0; $i -lt 16; $i++) {
    $shardName = "damper.message.queue.shard_$($i.ToString('D2'))"
    
    # Create Queue
    $body = @{
        durable = $true
        arguments = @{
            "x-queue-type" = "quorum"
            "x-dead-letter-exchange" = "damper.dlx"
            "x-dead-letter-routing-key" = "dead-letter"
        }
    }
    Invoke-RestMethod -Uri "$baseUri/queues/%2f/$shardName" -Method Put -Headers $headers -Body ($body | ConvertTo-Json -Depth 5) -ContentType "application/json"
    
    # Bind to Consistent Hash Exchange (Raw JSON string avoids PowerShell hashtable serialization bugs)
    $bindJson = '{"routing_key":"10","arguments":{}}'
    Invoke-RestMethod -Uri "$baseUri/bindings/%2f/e/damper.message.exchange/q/$shardName" -Method Post -Headers $headers -Body $bindJson -ContentType "application/json"
    
    Write-Host "Provisioned & Bound: $shardName"
}

# 5. PROVISION & BIND: Parking Lot Exchange & Queue (delayed automatic retry for suspended/failed customers)
Invoke-RestMethod -Uri "$baseUri/exchanges/%2f/damper.parking.exchange" -Method Put -Headers $headers -Body (@{
    type = "fanout"; durable = $true
} | ConvertTo-Json) -ContentType "application/json"

$parkingBody = @{
    durable = $true
    arguments = @{
        "x-queue-type" = "quorum"
        "x-dead-letter-exchange" = "damper.message.exchange"
        # Deliberately NOT setting x-dead-letter-routing-key here - the per-message
        # routing key (customer ID) must survive the bounce-back so the consistent-hash
        # exchange re-shards it correctly when the TTL expires.
    }
}
Invoke-RestMethod -Uri "$baseUri/queues/%2f/damper.message.queue.parking" -Method Put -Headers $headers -Body ($parkingBody | ConvertTo-Json -Depth 5) -ContentType "application/json"

# Fanout binding needs no routing key - it delivers regardless, while the customer ID
# routing key used at publish time still travels with the message for later dead-lettering.
Invoke-RestMethod -Uri "$baseUri/bindings/%2f/e/damper.parking.exchange/q/damper.message.queue.parking" -Method Post -Headers $headers -Body (@{} | ConvertTo-Json) -ContentType "application/json"

Write-Host "Provisioned & Bound: Parking Lot Exchange & Queue"

Write-Host "Infrastructure setup complete successfully." -ForegroundColor Green