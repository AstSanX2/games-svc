# Games Service (`games-svc`)

Serviço responsável por **catálogo de jogos**, **busca**, **Top 10** e **recomendações**, além de **criar compras** (purchase) e disparar o fluxo assíncrono de pagamento.

Este projeto roda como **API containerizada** no **Kubernetes/EKS**, persiste em **MongoDB**, e usa **OpenSearch/Elasticsearch** para busca/recomendação (com fallback para Mongo quando não configurado).

## Arquitetura (visão rápida)

```
Client → API Gateway HTTP API (/games/*)
              ↓
          games-api (EKS)
           ├─ MongoDB (Games/Purchases/DomainEvents/Outbox)
           └─ OpenSearch (search/recommendation)

games-api → Outbox → OutboxPublisher → SQS games-events-queue → games-worker
games-api → Outbox → OutboxPublisher → SQS payments-queue     → payments-worker
```

## Endpoints (principais)
- `GET /health`, `GET /ready`
- `GET /api/Game`, `GET /api/Game/{id}`
- `POST/PUT/DELETE /api/Game/*` (admin)
- `GET /api/Game/search` (busca)
- `GET /api/Game/popular?top=10` (Top 10)
- `GET /api/Game/recommendations` (recomendação)
- `POST /api/Purchases` (cria purchase `PENDING` e enfileira `PaymentInitiated` via outbox)

## Busca e recomendações (OpenSearch)
- O serviço usa o provider `IGameSearchProvider`:
  - `ElasticGameSearchProvider` quando `Elastic` estiver configurado
  - `FallbackMongoGameSearchProvider` quando não estiver configurado

Configuração (exemplo):

```json
{
  "Elastic": {
    "Url": "http://localhost:9200",
    "IndexName": "games",
    "Username": "",
    "Password": "",
    "ApiKey": ""
  }
}
```

## Eventos (SQS) + Outbox
Publicação é feita via **Outbox pattern** (a API grava uma `OutboxMessage` no Mongo e um HostedService publica no SQS).

- **games-events-queue** (+ DLQ `games-events-dlq`)
  - `GameStarted`, `GameQueued`
- **payments-queue** (+ DLQ `payments-dlq`)
  - `PaymentInitiated` (gerado a partir de `PurchaseService.CreateAsync`)

O formato padrão de mensagem é `Domain.Events.IntegrationEventEnvelope`.

## Configuração (Kubernetes)
- O pod espera um `Secret` **`games-appsettings`** contendo `appsettings.Production.json` montado em `/app/appsettings.Production.json`.
- Passo-a-passo: `fcg-domain/k8s/SECRETS.md`
- Manifests do serviço: `fcg-domain/k8s/games/*`

## Testes

```bash
dotnet test test/games-svc.Tests/games-svc.Tests.csproj -c Release
```

## Docker
- API: `src/games-svc/Dockerfile`
- Worker: `src/Worker/Dockerfile`
- Imagens rodam como **non-root** (`USER app`) e sem `HEALTHCHECK` (probes são do Kubernetes).


