| Term                        | Meaning                                                                     |
| --------------------------- | --------------------------------------------------------------------------- |
| **Integration**             | The thing a Damper administrator creates and manages.                       |
| **Ingress Endpoint**        | The HTTP endpoint that accepts incoming requests and authenticates them.    |
| **Message**                 | The payload being transported through Damper. It is opaque to the system.   |
| **Message Envelope**        | The immutable transport object containing the message plus Damper metadata. |
| **Message Route**           | The configuration that determines how accepted messages are delivered.      |
| **Destination**             | Where a message is ultimately sent.                                         |
| **Delivery Settings**       | Rate limits, retries, timeouts, and other delivery behavior.                |
| **Ingress Authentication**  | How incoming requests are authenticated.                                    |
| **Outbound Authentication** | How Damper authenticates to the destination.                                |


The Product

Damper is a configurable message ingress and delivery engine.

That's probably my favorite one-sentence description of the product.

It doesn't lock you into webhooks forever.

It doesn't say HTTP.

It doesn't say RabbitMQ.

It says exactly what Damper does.

The Domain Model

An Integration defines how Damper accepts messages (Ingress) and how it sends them (Delivery).

I think this is the sentence that finally unlocked the domain model.

Everything hangs off of that.

Integration
├── Ingress
└── Delivery

Simple.

Elegant.

Easy to explain.

The Mental Model

Every message enters through an Ingress and leaves through a Delivery.

That's Damper in one sentence.

No mention of customers.

No mention of queues.

No implementation details.

The Business Process

Create an Integration. Configure its Ingress and Delivery. Damper handles the rest.

I could honestly see this on the home page.

The Operational Promise

Messages flow between Ingress and Delivery reliably, at the rate you control.

That says what customers actually buy.

Not RabbitMQ.

Not PostgreSQL.

They buy reliability and rate control.

The Architectural Principle

This one is more for us than your customers.

Damper doesn't care who your customers are. It cares how messages enter and how they are delivered.

I think this realization simplified the entire architecture.

We eliminated:

CustomerConfig
CustomerId
CustomerRepository

because they weren't actually domain concepts.

The Aggregate

An Integration is the unit of configuration.

Everything belongs to an Integration.

Ingress
Delivery
Settings
Authentication
Headers

One aggregate.

One lifecycle.

One repository.

The Runtime

The API key identifies the Integration. The Integration defines the Delivery.

That sentence explains the entire runtime.

HTTP Request
     │
     ▼
 API Key
     │
     ▼
 Integration
     │
     ▼
  Delivery
The Product Boundary

This was another big realization.

Damper moves messages. It does not understand them.

That may become the defining philosophy of the product.

Payloads are opaque.

Headers are preserved.

Bodies are bytes.

Damper is transport.

The Design Philosophy

I think this one has emerged over months of discussion.

Model the business, not the implementation.

That's exactly why we've renamed things:

CustomerConfig → Integration
WebhookEnvelope → MessageEnvelope
WebhookRoute → Delivery
IngressEndpoint → Ingress

The implementation became simpler because the model became truer.

My Favorite

If I had to pick one sentence that encapsulates everything we've done, it would be this:

An Integration defines how Damper accepts messages and how it delivers them.

That sentence is architecture.

That sentence is documentation.

That sentence is your PostgreSQL schema.

That sentence is your REST API.

That sentence is your object model.

And perhaps most importantly, that sentence is something a new developer could read on day one and immediately understand the purpose of the core aggregate.

There's one more that I don't think we've said explicitly before, but I think it captures the spirit of the product:

Damper separates message acceptance from message delivery.

That separation is the heart of the system. Everything else—RabbitMQ, caching, PostgreSQL, retries, rate limiting, dead-letter handling—is in service of that one idea. Once you see Damper through that lens, almost every architectural decision becomes easier because you can ask, "Does this belong to acceptance (Ingress), delivery (Delivery), or the message flowing between them?" That's a remarkably clean conceptual foundation for the product.