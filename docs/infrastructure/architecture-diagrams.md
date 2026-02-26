# Architecture Diagrams

Visual diagrams for the Nutrir application architecture. Rendered using [Mermaid](https://mermaid.js.org/).

## Table of Contents

- [System Context (C4 Level 1)](#system-context)
- [Container Diagram (C4 Level 2)](#container-diagram)
- [Domain Model (ER Diagram)](#domain-model)
- [Middleware Pipeline](#middleware-pipeline)
- [AI Assistant Request Flow](#ai-assistant-request-flow)
- [Project Dependency Graph](#project-dependency-graph)

---

## System Context

High-level view of Nutrir and its external dependencies.

```mermaid
C4Context
    title Nutrir - System Context

    Person(nutritionist, "Nutritionist", "Primary user managing clients, meals, and appointments")
    Person(admin, "Admin", "Manages users, system settings, and maintenance")

    Enterprise_Boundary(nutrir, "Nutrir Platform") {
        System(app, "Nutrir Web App", "Blazor Server application for nutrition practice management")
    }

    System_Ext(google, "Google OAuth", "External identity provider")
    System_Ext(microsoft, "Microsoft OAuth", "External identity provider")
    System_Ext(anthropic, "Anthropic Claude API", "AI assistant for client and meal plan management")
    System_Ext(seq, "Seq", "Structured log aggregation and search")
    SystemDb_Ext(postgres, "PostgreSQL", "Primary relational database")

    Rel(nutritionist, app, "Uses", "HTTPS / WebSocket")
    Rel(admin, app, "Manages", "HTTPS / WebSocket")
    Rel(app, google, "Authenticates via", "OAuth 2.0")
    Rel(app, microsoft, "Authenticates via", "OAuth 2.0")
    Rel(app, anthropic, "AI queries", "HTTPS / Streaming")
    Rel(app, seq, "Sends logs", "HTTP")
    Rel(app, postgres, "Reads/Writes", "TCP / EF Core")
```

---

## Container Diagram

Internal containers and services within the Nutrir platform.

```mermaid
C4Container
    title Nutrir - Container Diagram

    Person(user, "Practitioner", "Nutritionist or Admin")

    System_Boundary(nutrir, "Nutrir Platform") {
        Container(blazor, "Blazor Server App", "ASP.NET Core 9 / Blazor Server", "Interactive UI served over SignalR with SSR for Identity pages")
        Container(cli, "Nutrir CLI", ".NET Console / System.CommandLine", "Command-line interface for CRUD operations and scripting")
        Container(aiAgent, "AI Agent Service", "Anthropic SDK / C#", "Streaming Claude assistant with 35+ tools for practice management")
        ContainerDb(db, "PostgreSQL 17", "Npgsql / EF Core", "Clients, appointments, meal plans, progress, audit logs, AI conversations")
        Container(seqSvc, "Seq", "Serilog Sink", "Structured log aggregation on ports 7101/7102")
    }

    System_Ext(anthropic, "Anthropic API", "Claude Haiku model")
    System_Ext(oauth, "OAuth Providers", "Google and Microsoft")

    Rel(user, blazor, "Uses", "HTTPS / WebSocket")
    Rel(user, cli, "Runs", "Terminal")
    Rel(blazor, db, "Reads/Writes", "EF Core / Npgsql")
    Rel(blazor, aiAgent, "Streams AI responses", "In-process")
    Rel(aiAgent, anthropic, "Tool loop", "HTTPS / SSE")
    Rel(aiAgent, db, "Executes tools against", "EF Core")
    Rel(cli, db, "Reads/Writes", "EF Core / Npgsql")
    Rel(blazor, oauth, "OIDC login", "HTTPS")
    Rel(blazor, seqSvc, "Logs to", "HTTP")
    Rel(cli, seqSvc, "Logs to", "HTTP")
```

---

## Domain Model

Entity relationships across all four v1 domains plus auth, compliance, and AI.

```mermaid
erDiagram
    APPLICATION_USER {
        guid Id PK
        string FirstName
        string LastName
        string Email UK
        bool IsActive
        datetime CreatedDate
        datetime LastLoginDate
    }

    CLIENT {
        guid Id PK
        string FirstName
        string LastName
        string Email
        string Phone
        datetime DateOfBirth
        guid PrimaryNutritionistId FK
        bool ConsentGiven
        datetime ConsentTimestamp
        string ConsentPolicyVersion
        bool IsDeleted
    }

    APPOINTMENT {
        guid Id PK
        guid ClientId FK
        guid NutritionistId FK
        enum Type
        enum Status
        datetime StartTime
        int DurationMinutes
        enum Location
        string VirtualMeetingUrl
        bool IsDeleted
    }

    MEAL_PLAN {
        guid Id PK
        guid ClientId FK
        guid CreatedByUserId FK
        string Title
        enum Status
        datetime StartDate
        datetime EndDate
        decimal CalorieTarget
        decimal ProteinTargetG
        decimal CarbsTargetG
        decimal FatTargetG
        bool IsDeleted
    }

    MEAL_PLAN_DAY {
        guid Id PK
        guid MealPlanId FK
        int DayNumber
        string Label
    }

    MEAL_SLOT {
        guid Id PK
        guid MealPlanDayId FK
        enum MealType
        int SortOrder
    }

    MEAL_ITEM {
        guid Id PK
        guid MealSlotId FK
        string FoodName
        string Quantity
        string Unit
        decimal CaloriesKcal
        decimal ProteinG
        decimal CarbsG
        decimal FatG
    }

    PROGRESS_GOAL {
        guid Id PK
        guid ClientId FK
        guid CreatedByUserId FK
        string Title
        enum GoalType
        decimal TargetValue
        string TargetUnit
        datetime TargetDate
        enum Status
        bool IsDeleted
    }

    PROGRESS_ENTRY {
        guid Id PK
        guid ClientId FK
        guid CreatedByUserId FK
        datetime EntryDate
        string Notes
        bool IsDeleted
    }

    PROGRESS_MEASUREMENT {
        guid Id PK
        guid ProgressEntryId FK
        enum MetricType
        string CustomMetricName
        decimal Value
        string Unit
    }

    CONSENT_EVENT {
        guid Id PK
        guid ClientId FK
        enum EventType
        string ConsentPurpose
        string PolicyVersion
        datetime Timestamp
        guid RecordedByUserId FK
    }

    CONSENT_FORM {
        guid Id PK
        guid ClientId FK
        string FormVersion
        datetime GeneratedAt
        enum SignatureMethod
        bool IsSigned
        datetime SignedAt
    }

    AUDIT_LOG_ENTRY {
        guid Id PK
        datetime Timestamp
        string UserId
        string Action
        string EntityType
        string EntityId
        enum Source
    }

    AI_CONVERSATION {
        guid Id PK
        string UserId FK
        datetime CreatedAt
        datetime LastMessageAt
    }

    AI_CONVERSATION_MESSAGE {
        guid Id PK
        guid ConversationId FK
        string Role
        string ContentJson
        string DisplayText
    }

    AI_USAGE_LOG {
        guid Id PK
        string UserId FK
        int InputTokens
        int OutputTokens
        int ToolCallCount
        string Model
    }

    APPLICATION_USER ||--o{ CLIENT : "manages"
    APPLICATION_USER ||--o{ APPOINTMENT : "conducts"
    CLIENT ||--o{ APPOINTMENT : "attends"
    CLIENT ||--o{ MEAL_PLAN : "receives"
    CLIENT ||--o{ PROGRESS_GOAL : "has"
    CLIENT ||--o{ PROGRESS_ENTRY : "records"
    CLIENT ||--o{ CONSENT_EVENT : "consents"
    CLIENT ||--o{ CONSENT_FORM : "signs"
    MEAL_PLAN ||--|{ MEAL_PLAN_DAY : "contains"
    MEAL_PLAN_DAY ||--|{ MEAL_SLOT : "has"
    MEAL_SLOT ||--o{ MEAL_ITEM : "includes"
    PROGRESS_ENTRY ||--|{ PROGRESS_MEASUREMENT : "measures"
    AI_CONVERSATION ||--|{ AI_CONVERSATION_MESSAGE : "contains"
    APPLICATION_USER ||--o{ AI_CONVERSATION : "chats"
```

---

## Middleware Pipeline

Request processing order through the ASP.NET Core middleware stack.

```mermaid
%%{init: {"theme": "default", "flowchart": {"curve": "basis"}}}%%
flowchart TD
    req([HTTP Request])

    serilog["Serilog Request Logging"]
    migrations["EF Migrations Endpoint\n#40;Dev only#41;"]
    hsts["HSTS\n#40;Production only#41;"]
    exception["Exception Handler\n/error/500"]
    https["HTTPS Redirection"]
    mfa["MFA Enforcement Middleware"]
    maintenance["Maintenance Mode Middleware"]
    statusCode["Status Code Pages\n/error/#123;code#125;"]
    antiforgery["Antiforgery"]
    staticAssets["Static Assets\n#40;CSS, JS, images#41;"]

    subgraph endpoints["Endpoint Routing"]
        direction TB
        blazorRoutes["Blazor Components\nInteractiveServer"]
        identity["Identity Endpoints\n#40;Login, Register, 2FA#41;"]
        consentApi["Consent Form API\n#40;PDF, DOCX, Sign, Scan#41;"]
        maintenanceApi["Maintenance API\n#40;Admin only#41;"]
        devEndpoints["Dev Endpoints\n#40;Dev only#41;"]
    end

    resp([HTTP Response])

    req --> serilog --> migrations --> hsts --> exception --> https
    https --> mfa
    mfa -->|"No 2FA setup"| resp
    mfa -->|"2FA OK / exempt"| maintenance
    maintenance -->|"Maintenance active\n#40;non-admin#41;"| resp
    maintenance -->|"Pass"| statusCode --> antiforgery --> staticAssets
    staticAssets --> endpoints
    blazorRoutes --> resp
    identity --> resp
    consentApi --> resp
    maintenanceApi --> resp
    devEndpoints --> resp
```

---

## AI Assistant Request Flow

Sequence of events when a user interacts with the AI assistant panel.

```mermaid
sequenceDiagram
    autonumber

    actor U as Practitioner
    participant Panel as AiAssistantPanel
    participant Agent as AiAgentService
    participant API as Anthropic API
    participant Tools as AiToolExecutor
    participant DB as PostgreSQL

    U ->> Panel: Types message and sends
    Panel ->> Agent: StreamResponseAsync#40;message#41;

    Agent ->> DB: Load conversation history
    DB -->> Agent: Previous messages

    Agent ->> API: Create message #40;streaming#41;
    Note right of API: Claude Haiku model with<br/>system prompt and tools

    loop Tool Use Loop
        API -->> Agent: Stream delta #40;text or tool_use#41;
        Agent -->> Panel: Yield text tokens
        Panel -->> U: Render streaming markdown

        opt Tool call received
            Agent ->> Tools: ExecuteToolAsync#40;name, args#41;

            alt Requires confirmation
                Tools -->> Agent: Needs user approval
                Agent -->> Panel: Show confirmation dialog
                Panel -->> U: Display tool action
                U ->> Panel: Approve / Deny
                Panel ->> Agent: Confirmation result
            end

            Tools ->> DB: Execute query / mutation
            DB -->> Tools: Result
            Tools -->> Agent: Tool result JSON
            Agent ->> API: Submit tool_result
            Note right of API: Continue generation<br/>with tool output
        end
    end

    API -->> Agent: stop_reason = end_turn
    Agent ->> DB: Save conversation + messages
    Agent ->> DB: Log usage #40;tokens, tools, duration#41;
    Agent -->> Panel: Stream complete
    Panel -->> U: Final rendered response
```

---

## Project Dependency Graph

How the four .NET projects reference each other.

```mermaid
flowchart TD
    subgraph solution["Nutrir.sln"]
        direction TB
        web["Nutrir.Web\n#40;Blazor Server, UI,\nMiddleware, Auth#41;"]
        infra["Nutrir.Infrastructure\n#40;EF Core, Services,\nAI Agent, Migrations#41;"]
        core["Nutrir.Core\n#40;Entities, Interfaces,\nDTOs, Enums#41;"]
        cli["Nutrir.Cli\n#40;System.CommandLine,\nCLI CRUD#41;"]
    end

    web --> infra
    web --> core
    infra --> core
    cli --> infra
    cli --> core

    subgraph external["External Dependencies"]
        direction LR
        ef["EF Core / Npgsql"]
        identity["ASP.NET Identity"]
        serilog["Serilog"]
        anthropicSdk["Anthropic SDK"]
        questpdf["QuestPDF"]
    end

    web -.-> identity
    web -.-> serilog
    infra -.-> ef
    infra -.-> anthropicSdk
    infra -.-> questpdf
```
