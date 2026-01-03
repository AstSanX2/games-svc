# Games Service (games-svc)

Serviço **Games** do FCG — **catálogo** de jogos, **busca** com **MongoDB Atlas Search**, **agregações** (populares), e **criação de compras** (publica evento em **SQS** para o Payments).  
Roda **serverless** em **AWS Lambda** atrás do **API Gateway (REST)**, usa **MongoDB Atlas** e **JWT** para segurança. **Observabilidade** via **AWS X-Ray** + **CloudWatch Logs**.

Este README cobre: como configurar **segredos**, **deploy**, **testar** (incluindo SQS), **ver traces** e **resolver problemas**.

---

## Sumário

- [Arquitetura (visão rápida)](#arquitetura-visão-rápida)
- [Stack / Tecnologias](#stack--tecnologias)
- [Rotas Principais](#rotas-principais)
- [Pré-requisitos](#pré-requisitos)
- [Configuração (appsettings)](#configuração-appsettings)
- [Configuração Local (Dev)](#configuração-local-dev)
- [Execução Local](#execução-local)
- [Deploy na AWS (Serverless)](#deploy-na-aws-serverless)
- [Observabilidade (X-Ray + Logs)](#observabilidade-x-ray--logs)
- [Atlas Search (índices)](#atlas-search-índices)
- [Segurança entre microsserviços (SQS Policy / IAM)](#segurança-entre-microsserviços-sqs-policy--iam)
- [Testes Rápidos (cURL)](#testes-rápidos-curl)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Políticas/IAM mínimas esperadas](#políticasiam-mínimas-esperadas)
- [Dicas e Troubleshooting](#dicas-e-troubleshooting)
- [Limpeza de Infra](#limpeza-de-infra)

---

## Arquitetura (visão rápida)

```
Client → API Gateway (REST proxy)
                ↓
           AWS Lambda (games-svc, .NET 8)
         ┌───────────────┴───────────────────┐
         ↓                                   ↓
   MongoDB Atlas (Games & Purchases)      Amazon SQS (payments-queue)
                                             ↓
                                     Payments Worker (Lambda)
```

- **Busca**: Atlas Search (índice `default`) e, opcionalmente, `autocomplete` para sugestão por prefixo.
- **Recomendações**: “similar a jogos comprados” (pipeline com `$search`/`moreLikeThis` e filtros).
- **Compra**: o endpoint **cria um registro** e **publica** o evento na **SQS** (`payments-queue`) processado pelo **Payments Worker**.
- **Segurança**: valida **JWT** emitido pelo serviço **Users** (mesmo issuer/audience/secret).

---

## Stack / Tecnologias

- **.NET 8** (ASP.NET Core + Controllers).
- **AWS Lambda** + **API Gateway (REST)**.
- **MongoDB Atlas** (`MongoDB.Driver`).
- **MongoDB Atlas Search** (agregações `$search`).
- **Amazon SQS** (producer): fila **payments-queue**.
- **JWT** (`Microsoft.AspNetCore.Authentication.JwtBearer`).
- Configuração por `appsettings`.
- **AWS X-Ray** (traces) + **CloudWatch Logs**.

---

## Rotas Principais

> Os caminhos usam o **Invoke URL** do API Gateway `API_G`.

### Health
| Método | Rota        | Auth    | Descrição                    |
|------:|-------------|---------|------------------------------|
| GET   | `/health`   | público | Health check simples.        |

### Catálogo de Jogos
| Método | Rota                    | Auth                | Descrição                                                                 |
|------:|-------------------------|---------------------|---------------------------------------------------------------------------|
| GET   | `/api/Game`             | público             | Lista paginada/filtrada (se aplicável).                                   |
| GET   | `/api/Game/{id}`        | público             | Detalhe do jogo.                                                          |
| POST  | `/api/Game`             | **Bearer (Admin)**  | Cria jogo (Name, Description, Category, ReleaseDate, Price).              |
| PUT   | `/api/Game/{id}`        | **Bearer (Admin)**  | Atualiza jogo.                                                            |
| DELETE| `/api/Game/{id}`        | **Bearer (Admin)**  | Remove jogo.                                                              |

### Busca e Recomendações
| Método | Rota                                | Auth     | Descrição                                                                                   |
|------:|-------------------------------------|----------|---------------------------------------------------------------------------------------------|
| GET   | `/api/Game/search?query=...`        | público  | Busca por texto (Atlas Search, índice `default`).                                           |
| GET   | `/api/Game/recommendations?limit=…` | **Bearer** | Recomenda jogos semelhantes aos já comprados pelo usuário autenticado.                     |
| GET   | `/api/Game/popular?top=10`          | público  | Agregação: jogos mais “vendidos” (contagem de compras).                                     |

### Compras
| Método | Rota             | Auth     | Descrição                                                                                   |
|------:|------------------|----------|---------------------------------------------------------------------------------------------|
| POST  | `/api/Purchases` | **Bearer** | Cria uma compra e **publica** o evento na **payments-queue** (processamento assíncrono).    |

> O **status** da compra é atualizado pelo **Payments Worker**; a **consulta de status** é feita na **Payments API**.

---

## Pré-requisitos

- **.NET 8 SDK**
- **AWS CLI** configurado:
  ```bash
  aws configure
  # region us-east-1, output json
  ```
- **Amazon.Lambda.Tools**:
  ```bash
  dotnet tool install -g Amazon.Lambda.Tools
  dotnet tool update -g Amazon.Lambda.Tools
  ```
- **MongoDB Atlas** (cluster e database definidos).
- **SQS**: fila `payments-queue` criada (ou será criada via IaC).

---

## Configuração (appsettings)

- **Local**: `appsettings.Development.json` no repositório.
- **Prod (Kubernetes)**: `appsettings.Production.json` montado no pod via `k8s/secrets.yaml` (chave `appsettings.Production.json`).

---

## Configuração Local (Dev)

Você pode definir **fallback** em `appsettings.Development.json`:

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb+srv://<user>:<pass>@<cluster>.mongodb.net/<db>?retryWrites=true&w=majority&appName=<app>"
  },
  "JwtOptions": {
    "Key": "<chave-aleatoria-32+>",
    "Issuer": "fcg-auth",
    "Audience": "fcg-clients"
  },
  "Payments": {
    "QueueUrl": "https://sqs.us-east-1.<account>.amazonaws.com/<accountId>/payments-queue"
  }
}
```

> Em produção (Kubernetes), os valores vêm do `appsettings.Production.json` montado no pod.

---

## Execução Local

```bash
dotnet restore
dotnet build
dotnet run
# http://localhost:5000 (ou conforme launchSettings.json)
```

- **Health**: `GET http://localhost:5000/health`  
- **Swagger**: `http://localhost:5000/swagger`

---

## Deploy na AWS (Serverless)

1. **Bucket de artifacts** (uma vez, se precisar):
   ```bash
   aws s3 mb s3://lambda-artifacts-games-fcg-us-east-1 --region us-east-1
   ```

2. **Deploy**:
   ```bash
   # na pasta src/games-svc
   dotnet lambda deploy-serverless
   # Stack name: games-svc
   # S3 Bucket: lambda-artifacts-games-fcg-us-east-1
   ```

3. **Invoke URL**:
   ```bash
   aws cloudformation describe-stacks \
     --stack-name games-svc \
     --query "Stacks[0].Outputs[?OutputKey=='ApiUrl' || OutputKey=='ApiURL'].OutputValue" \
     --output text
   ```

> **X-Ray**: você pode definir `"Tracing": "Active"` no template e garantir a policy `AWSXRayDaemonWriteAccess` na role.  
> **SQS**: a role do games-svc precisa de `sqs:SendMessage` na fila `payments-queue`.

---

## Observabilidade (X-Ray + Logs)

### Habilitar X-Ray
- Lambda (games-svc) → **Configuration → Monitoring and operations tools → AWS X-Ray → Active tracing**.
- API Gateway (games) → **Stages (Prod) → Logs/Tracing → X-Ray Tracing = Enabled**.
- (Opcional) `AWS_XRAY_TRACING_NAME=games-svc` (env var) para nome amigável.

### Ver o Service Map end-to-end
- Gere o fluxo **busca → compra → status**.  
- X-Ray → **Service map** deve mostrar: **API GW → games-svc → SQS → payments-worker → Atlas**.

### Logs (CloudWatch)
```bash
aws logs tail /aws/lambda/<NOME-DA-FUNÇÃO-GAMES> --follow
```

> Se quiser que o **nó SQS** apareça explicitamente, habilite a **instrumentação do AWS SDK** no código (detalhado no repositório raiz).

---

## Atlas Search (índices)

### Índice `default` (dinâmico)
- Atlas → **Data Explorer** → sua **collection `Game`** → **Search** → **Create Search Index**.
- **Index name**: `default`  
- **Analyzer**: padrão  
- **Mapping**: **Dynamic** (habilite para todos os campos de texto).  
- Salvar e aguardar o **status “Active”**.

### (Opcional) Índice `autocomplete`
- Crie um segundo índice com **mapeamento `autocomplete`** para o campo `Name`.  
- Útil para `queryType: "autocomplete"` (sugestões por prefixo).

> Se usar `moreLikeThis`, **não** envie a subchave `options` (alguns clusters não aceitam).

---

## Segurança entre microsserviços (SQS Policy / IAM)

- **IAM da Lambda games-svc**: precisa de `sqs:SendMessage` **apenas** na fila `payments-queue`.
- **Queue Policy** da fila `payments-queue`:
  - **Permitir** `sqs:SendMessage` **somente** para o **principal (role) do games-svc**.
  - **Permitir** `sqs:ReceiveMessage/DeleteMessage` **somente** para o **principal (role) do payments-worker**.

> A política da **fila** é **recurso** (resource-based) e restringe quem pode usar. Documente os ARNs usados no seu README raiz.

---

## Testes Rápidos (cURL)

> Requer **token** do serviço **Users** (Issuer/Audience/Secret idênticos nos 3 serviços).

### 0) Variáveis
```bash
export API_G="https://<api-id>.execute-api.us-east-1.amazonaws.com/Prod"
export TOKEN="<jwt>"
```

### 1) Criar jogo (Admin)
```bash
curl -X POST "$API_G/api/Game" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name":"Elder Stars",
    "description":"RPG espacial com exploração profunda",
    "category":"RPG",
    "releaseDate":"2024-11-01T00:00:00Z",
    "price":199.90
  }'
```

### 2) Buscar jogos (texto)
```bash
curl "$API_G/api/Game/search?query=RPG"
```

### 3) Populares
```bash
curl "$API_G/api/Game/popular?top=5"
```

### 4) Recomendações (com base no histórico)
```bash
curl -H "Authorization: Bearer $TOKEN" "$API_G/api/Game/recommendations?limit=10"
```

### 5) Criar compra (publica na SQS)
```bash
curl -X POST "$API_G/api/Purchases" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "gameId":"<ObjectId-do-jogo>" }'
```

> Acompanhe **CloudWatch Logs** do **games-svc**, e **métricas** / **mensagens** na fila **SQS**.  
> O **status** da compra é atualizado pelo **Payments Worker** e exposto via **Payments API**.

---

## Estrutura do Projeto

```
src/games-svc/
  Application/
    DTO/ (Game, Purchase, filtros e projeções)
    Services/GameService.cs
    Services/PurchaseService.cs
  Domain/
    Entities/Game.cs
    Entities/Purchase.cs
    Interfaces/Repositories/IGameRepository.cs
    Interfaces/Repositories/IPurchaseRepository.cs
    Interfaces/Services/IGameService.cs
    Interfaces/Services/IPurchaseService.cs
    Models/Response/ResponseModel.cs
  Infraestructure/
    Repositories/BaseRepository.cs
    Repositories/GameRepository.cs
    Repositories/PurchaseRepository.cs
  Controllers/
    GameController.cs
    PurchasesController.cs
  Helpers/
    ObjectIdJsonConverter.cs
  Program.cs
  aws-lambda-tools-defaults.json
  serverless.template
  appsettings*.json
  games-svc.csproj
```

---

## Políticas/IAM mínimas esperadas

- **Role da Lambda (games-svc)**:
  - `AWSLambdaBasicExecutionRole` (logs)
  - `AWSXRayDaemonWriteAccess` (traces)
  - (Kubernetes) use Secrets/ConfigMaps para configurações e segredos.
  - **SQS**: policy **apenas** para `sqs:SendMessage` na fila `payments-queue`.

- **Queue policy** (recurso SQS):
  - Permite **SendMessage** apenas para **role do games-svc**.
  - Permite **Receive/Delete** apenas para **role do payments-worker**.

> **Princípio do menor privilégio**: evite permissões genéricas (`*FullAccess`).

---

## Dicas e Troubleshooting

**1) `InvalidAddressException: The address arn:aws:sqs... is not valid for this endpoint`**  
- Use **Queue URL** com o **cliente SQS**; ARNs servem para policies, não para envio de mensagem.  
- Confirme **região** do endpoint e da fila.

**2) `AccessDenied` ao enviar para SQS**  
- Verifique **policy da role** (games-svc) e a **Queue Policy** do recurso SQS.  
- Teste negativo com usuário sem permissão para validar segurança.

**3) `401 Unauthorized`**  
- **Issuer/Audience/Secret** devem ser **idênticos** aos do **Users**.  
- Token expirado (verifique `exp`).

**4) `Database name must be specified in the connection string`**  
- Inclua o **nome do DB** na URI do Atlas.

**5) Atlas Search `moreLikeThis`: `"unrecognized field 'options'"`**  
- Remova a subchave `options` do operador `moreLikeThis` (algumas versões do Search não suportam).

**6) X-Ray sem nó SQS**  
- Ative **Active tracing** e, se quiser ver o nó do SQS, habilite a **instrumentação do AWS SDK** (ver doc do repositório raiz).

**7) `VisibilityTimeout < Function timeout` ao criar o evento SQS→Lambda**  
- Defina **VisibilityTimeout** da fila **maior** que o timeout da Lambda consumidora (ex.: fila 360s; worker 60–120s).

---

## Limpeza de Infra

```bash
aws cloudformation delete-stack --stack-name games-svc
# (E limpe a fila se foi criada fora do stack)
```
