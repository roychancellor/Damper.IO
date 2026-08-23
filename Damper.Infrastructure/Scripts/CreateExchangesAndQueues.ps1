param (
    [string]$HostName = "localhost",

    [int]$Port = 15672,

    [Parameter(Mandatory = $true)]
    [string]$User,

    [Parameter(Mandatory = $true)]
    [string]$Password,

    [switch]$Reset
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Damper RabbitMQ Infrastructure
# ============================================================================
#
# Creates the RabbitMQ exchanges, queues, and bindings required by Damper.
#
# By default, this script is NON-DESTRUCTIVE. Existing compatible RabbitMQ
# resources are left in place.
#
# Use -Reset only when you intentionally want to delete and recreate the
# Damper RabbitMQ topology.
#
# Requires the RabbitMQ consistent-hash exchange plugin:
#
#   rabbitmq_consistent_hash_exchange
#
# ============================================================================

$VirtualHost = "%2f"
$ShardCount = 16
$ShardBindingWeight = "10"

$MessageExchange = "damper.message.exchange"
$DeadLetterExchange = "damper.dlx"
$ParkingExchange = "damper.parking.exchange"

$DeadLetterQueue = "damper.message.queue.dead_letter"
$ParkingQueue = "damper.message.queue.parking"

# ============================================================================
# Authentication / API Setup
# ============================================================================

$pair = "${User}:${Password}"
$bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [System.Convert]::ToBase64String($bytes)

$headers = @{
    Authorization = "Basic $base64"
}

$baseUri = "http://${HostName}:${Port}/api"


# ============================================================================
# Helper Functions
# ============================================================================

function Write-Section {
    param (
        [string]$Message
    )

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}


function Remove-RabbitMqResource {
    param (
        [string]$Uri,
        [string]$Description
    )

    try {
        Invoke-RestMethod `
            -Uri $Uri `
            -Method Delete `
            -Headers $headers `
            -ErrorAction Stop | Out-Null

        Write-Host "Removed: $Description" -ForegroundColor Yellow
    }
    catch {
        # DELETE returns an error when the resource does not exist.
        # During reset that is harmless, so intentionally ignore it.
    }
}


# ============================================================================
# Verify RabbitMQ Management API
# ============================================================================

Write-Section "Verifying RabbitMQ"

try {
    $overview = Invoke-RestMethod `
        -Uri "$baseUri/overview" `
        -Method Get `
        -Headers $headers

    Write-Host "Connected to RabbitMQ $($overview.rabbitmq_version)"
}
catch {
    throw @"
Unable to connect to the RabbitMQ Management API.

Host : $HostName
Port : $Port
User : $User

Verify that RabbitMQ is running, the management plugin is enabled,
and the supplied credentials are correct.

$($_.Exception.Message)
"@
}


# ============================================================================
# Optional Destructive Reset
# ============================================================================

if ($Reset) {

    Write-Section "Resetting Existing Damper Infrastructure"

    Write-Host "WARNING: Existing Damper RabbitMQ resources will be deleted." `
        -ForegroundColor Yellow

    # Delete exchanges.
    Remove-RabbitMqResource `
        "$baseUri/exchanges/$VirtualHost/$MessageExchange" `
        $MessageExchange

    Remove-RabbitMqResource `
        "$baseUri/exchanges/$VirtualHost/$ParkingExchange" `
        $ParkingExchange

    Remove-RabbitMqResource `
        "$baseUri/exchanges/$VirtualHost/$DeadLetterExchange" `
        $DeadLetterExchange

    # Delete shard queues.
    for ($i = 0; $i -lt $ShardCount; $i++) {

        $shardName = "damper.message.queue.shard_$($i.ToString('D2'))"

        Remove-RabbitMqResource `
            "$baseUri/queues/$VirtualHost/$shardName" `
            $shardName
    }

    # Delete special-purpose queues.
    Remove-RabbitMqResource `
        "$baseUri/queues/$VirtualHost/$DeadLetterQueue" `
        $DeadLetterQueue

    Remove-RabbitMqResource `
        "$baseUri/queues/$VirtualHost/$ParkingQueue" `
        $ParkingQueue
}


# ============================================================================
# Dead Letter Infrastructure
# ============================================================================

Write-Section "Provisioning Dead Letter Infrastructure"

$deadLetterExchangeBody = @{
    type       = "direct"
    durable    = $true
    auto_delete = $false
    internal   = $false
    arguments  = @{}
}

Invoke-RestMethod `
    -Uri "$baseUri/exchanges/$VirtualHost/$DeadLetterExchange" `
    -Method Put `
    -Headers $headers `
    -Body ($deadLetterExchangeBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null


$deadLetterQueueBody = @{
    durable     = $true
    auto_delete = $false

    arguments = @{
        "x-queue-type" = "quorum"
    }
}

Invoke-RestMethod `
    -Uri "$baseUri/queues/$VirtualHost/$DeadLetterQueue" `
    -Method Put `
    -Headers $headers `
    -Body ($deadLetterQueueBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null


$deadLetterBindingBody = @{
    routing_key = "dead-letter"
    arguments   = @{}
}

Invoke-RestMethod `
    -Uri "$baseUri/bindings/$VirtualHost/e/$DeadLetterExchange/q/$DeadLetterQueue" `
    -Method Post `
    -Headers $headers `
    -Body ($deadLetterBindingBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null

Write-Host "Provisioned: $DeadLetterExchange"
Write-Host "Provisioned: $DeadLetterQueue"


# ============================================================================
# Main Consistent Hash Exchange
# ============================================================================

Write-Section "Provisioning Message Exchange"

$messageExchangeBody = @{
    type        = "x-consistent-hash"
    durable     = $true
    auto_delete = $false
    internal    = $false
    arguments   = @{}
}

Invoke-RestMethod `
    -Uri "$baseUri/exchanges/$VirtualHost/$MessageExchange" `
    -Method Put `
    -Headers $headers `
    -Body ($messageExchangeBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null

Write-Host "Provisioned: $MessageExchange"


# ============================================================================
# Shard Queues
# ============================================================================

Write-Section "Provisioning Message Shards"

for ($i = 0; $i -lt $ShardCount; $i++) {

    $shardName = "damper.message.queue.shard_$($i.ToString('D2'))"

    $shardQueueBody = @{
        durable     = $true
        auto_delete = $false

        arguments = @{
            "x-queue-type"                = "quorum"
            "x-dead-letter-exchange"      = $DeadLetterExchange
            "x-dead-letter-routing-key"   = "dead-letter"
        }
    }

    Invoke-RestMethod `
        -Uri "$baseUri/queues/$VirtualHost/$shardName" `
        -Method Put `
        -Headers $headers `
        -Body ($shardQueueBody | ConvertTo-Json -Depth 5) `
        -ContentType "application/json" |
        Out-Null


    # For an x-consistent-hash exchange the binding routing key represents
    # the queue's weight, not a message routing key.
    #
    # Every shard receives the same weight so traffic is distributed evenly.
    $shardBindingBody = @{
        routing_key = $ShardBindingWeight
        arguments   = @{}
    }

    Invoke-RestMethod `
        -Uri "$baseUri/bindings/$VirtualHost/e/$MessageExchange/q/$shardName" `
        -Method Post `
        -Headers $headers `
        -Body ($shardBindingBody | ConvertTo-Json -Depth 5) `
        -ContentType "application/json" |
        Out-Null

    Write-Host "Provisioned & Bound: $shardName"
}


# ============================================================================
# Parking Lot Infrastructure
# ============================================================================

Write-Section "Provisioning Parking Lot Infrastructure"

$parkingExchangeBody = @{
    type        = "fanout"
    durable     = $true
    auto_delete = $false
    internal    = $false
    arguments   = @{}
}

Invoke-RestMethod `
    -Uri "$baseUri/exchanges/$VirtualHost/$ParkingExchange" `
    -Method Put `
    -Headers $headers `
    -Body ($parkingExchangeBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null


$parkingQueueBody = @{
    durable     = $true
    auto_delete = $false

    arguments = @{
        "x-queue-type"           = "quorum"
        "x-dead-letter-exchange" = $MessageExchange

        # Deliberately do NOT specify x-dead-letter-routing-key.
        #
        # The original per-message routing key (Integration ID) must survive
        # the trip through the parking queue. When the message expires and is
        # dead-lettered back to the consistent-hash exchange, RabbitMQ can
        # therefore route it back to the correct shard.
    }
}

Invoke-RestMethod `
    -Uri "$baseUri/queues/$VirtualHost/$ParkingQueue" `
    -Method Put `
    -Headers $headers `
    -Body ($parkingQueueBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null


# The parking exchange is fanout, so no routing key is required for the
# exchange-to-queue binding. The message's original routing key is retained
# for its later dead-letter trip back to the message exchange.
$parkingBindingBody = @{
    routing_key = ""
    arguments   = @{}
}

Invoke-RestMethod `
    -Uri "$baseUri/bindings/$VirtualHost/e/$ParkingExchange/q/$ParkingQueue" `
    -Method Post `
    -Headers $headers `
    -Body ($parkingBindingBody | ConvertTo-Json -Depth 5) `
    -ContentType "application/json" |
    Out-Null

Write-Host "Provisioned: $ParkingExchange"
Write-Host "Provisioned & Bound: $ParkingQueue"


# ============================================================================
# Complete
# ============================================================================

Write-Section "Damper RabbitMQ Infrastructure Complete"

Write-Host "Exchanges:"
Write-Host "  $MessageExchange"
Write-Host "  $DeadLetterExchange"
Write-Host "  $ParkingExchange"

Write-Host ""
Write-Host "Queues:"
Write-Host "  $ShardCount message shard queues"
Write-Host "  $DeadLetterQueue"
Write-Host "  $ParkingQueue"

Write-Host ""
Write-Host "RabbitMQ infrastructure provisioned successfully." -ForegroundColor Green