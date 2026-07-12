# AGENTS.md - AI Agent Guidelines

**Repository Purpose**: 
This repository contains code samples for various Microsoft services and technologies, organized by domain and layered architecture. 
It serves as a reference implementation for best practices in C#, .NET, Azure integration, and engineering excellence.
This repository is maintained by the Architectural team to provide guidance, prove out ideas, and assess multiple options and scenarios.

**Audience**: 
The primary users and maintainers are Cloud Architects, with supporting contributions from engineering teams.

---

## Quick Start for AI Agents

This document guides AI agents (GitHub Copilot, code generators, etc.) on how to work effectively within the code-samples repository. For comprehensive project-specific guidelines, see `.github/copilot-instructions.md`.

**This guide covers:**
- Architecture principles and patterns
- C# and .NET standards
- Code quality and reliability expectations
- Common development workflows
- Azure integration patterns
- When to ask for clarification

---

## Primary Languages & Frameworks

### Core Stack
- **Language**: C# 14 with modern features enabled
- **Runtime**: .NET 10 (cross-platform)
- **Web Framework**: ASP.NET Core (including OData support where applicable)
- **Database Access**: Entity Framework Core (v10)
- **Cloud Platform**: Azure (multiple services: Azure Data Explorer, Cosmos DB, Event Hubs, App Configuration, Application Insights, etc.)
- **Testing**: xUnit framework
- **Mapping**: AutoMapper for DTO/contract transformations
- **Dependency Injection**: Built-in .NET Core DI with service registration in `Program.cs`

### Repository Structure

The solution follows a **domain-driven, layered architecture**:

```
code-samples/
├── [Domain].Contracts/           (DTOs, API request/response models, enums)
├── [Domain].Entities/            (EF Core entity models, domain models)
├── [Domain].Repository/          (Data access interfaces & implementations)
├── [Domain].Service/             (Business logic and service layer)
├── [Domain].Automapper.Profiles/ (DTO mapping profiles)
├── [Domain].DAL/                 (DbContext and EF Core configuration)
├── [ApiName].API/                (ASP.NET Core controllers, entry points)
├── Foundation.Common.*/          (Shared utilities, contracts, generators)
├── *.Tests/                      (xUnit test projects)
└── .github/                      (Repo configuration, workflows, guidelines)
```

### Key Architectural Principles

1. **Layered by Function, Organized by Domain**
   - Each domain (Audit, USM, etc.) has separate contracts, entities, repositories, and services
   - Shared concerns live in `Foundation.Common.*` packages
   - Avoid cross-domain dependencies; use shared contracts only

2. **Dependency Inversion**
   - Always depend on interfaces, not concrete implementations
   - Register all services in `Program.cs` via DI
   - Constructor inject dependencies explicitly

3. **Single Responsibility**
   - Contracts/DTOs: Define data shapes
   - Entities: Domain models and EF Core configuration
   - Repositories: Data access only (no business logic)
   - Services: Business logic, orchestration, external integrations
   - Controllers: HTTP handling, routing, status codes
   - Mappers: DTO transformations

4. **Async-First Design**
   - All I/O operations are async
   - Propagate cancellation tokens through call stacks
   - No sync-over-async patterns (`.Result`, `.GetAwaiter().GetResult()`)

---

## C# & .NET Standards

### Language Features (Mandatory)

- **Nullable reference types**: All projects have `<Nullable>enable</Nullable>`
  - Fix all nullability warnings; don't suppress them
  - Use `ArgumentNullException.ThrowIfNull()` for guard clauses
- **Implicit usings**: All projects have `<ImplicitUsings>enable</ImplicitUsings>`
- **File-scoped namespaces**: Use `namespace Domain.Layer;` (no braces)
- **Modern C# features**: Leverage records, pattern matching, and expression-bodied members

### Naming Conventions

| Entity | Convention | Example |
|--------|-----------|---------|
| Classes | `PascalCase` | `AuditService`, `UserRepository` |
| Interfaces | Prefix `I` | `IAuditService`, `IUserRepository` |
| Methods | `PascalCase` | `GetByIdAsync()`, `ExecuteQueryAsync()` |
| Properties | `PascalCase` | `AuditType`, `CreatedDate` |
| Constants | `UPPER_SNAKE_CASE` | `MAX_RETRY_COUNT`, `DEFAULT_TIMEOUT_MS` |
| Parameters | `camelCase` | `userId`, `cancellationToken` |
| Private fields | `_camelCase` | `_logger`, `_repository` |
| Local variables | `camelCase` | `result`, `request` |

### Async/Await Rules (Non-Negotiable)

1. **All I/O methods must be async**
   ```csharp
   // ✓ Good
   public async Task<User> GetUserAsync(string id, CancellationToken cancellationToken = default)
   
   // ✗ Bad: Sync wrapper around async, missing naming
   public User GetUser(string id)
   ```

2. **Method naming**: Async methods **must** end with `Async`
   ```csharp
   // ✓ Good
   var user = await GetUserAsync(id, cancellationToken);
   
   // ✗ Bad: Missing Async suffix
   var user = await GetUser(id);
   ```

3. **Always await async calls**
   ```csharp
   // ✓ Good
   var result = await _service.ProcessAsync(data, cancellationToken);
   
   // ✗ Bad: Fire-and-forget, unobserved task
   _service.ProcessAsync(data, cancellationToken);
   ```

4. **Cancellation tokens throughout**
   ```csharp
   public async Task<T> GetAsync(string id, CancellationToken cancellationToken = default)
   {
       cancellationToken.ThrowIfCancellationRequested();
       return await _provider.FetchAsync(id, cancellationToken);
   }
   ```

5. **No `ConfigureAwait(false)` in ASP.NET Core**
   - Omit in controllers, services, middleware
   - Only use in library/utility code where synchronization context is unneeded

---

## Code Quality & Reliability Patterns

### Dependency Injection & Service Registration

Register services in `Program.cs` following this pattern:

```csharp
// Services (business logic)
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// Repositories (data access)
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();

// External clients (Azure, third-party)
builder.Services.AddSingleton(sp => new CosmoClient(...));
builder.Services.AddScoped<IKustoClient, KustoClient>();

// Mappers
builder.Services.AddAutoMapper(typeof(UserProfile), typeof(AuditProfile));

// Logging is injected into constructors
// ILogger<T> is resolved automatically via DI
```

**Constructor injection pattern:**
```csharp
public class UserService
{
    private readonly IUserRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<UserService> _logger;
    
    public UserService(
        IUserRepository repository,
        IMapper mapper,
        ILogger<UserService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(logger);
        
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }
}
```

### Structured Logging

Use `ILogger<T>` in all services, controllers, and repositories:

```csharp
// ✓ Good: Contextual, structured properties
_logger.LogInformation("Processing user {UserId} from domain {Domain}", userId, domain);

try
{
    var result = await _repository.GetAsync(userId, cancellationToken);
    _logger.LogDebug("Retrieved user record");
    return result;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error retrieving user {UserId}", userId);
    throw;
}

// ✗ Bad: String concatenation, no context
_logger.LogInformation("Processing user");
```

**Log levels:**
- `LogDebug`: Internal state, loop iterations (development/tracing)
- `LogInformation`: Request received, operation milestones, decisions
- `LogWarning`: Validation failures, expected exceptions, recoverable issues
- `LogError`: Unhandled exceptions, unrecoverable errors
- Never log sensitive data (passwords, tokens, PII)

### Validation & Guard Clauses

Validate inputs early, at the service/controller boundary:

```csharp
// ✓ Good: Guard clauses at method entry
public async Task<User> ProcessUserAsync(UserRequest request, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(request);
    
    if (string.IsNullOrWhiteSpace(request.UserId))
        throw new ArgumentException("UserId is required", nameof(request.UserId));
    
    if (request.StartDate > request.EndDate)
        throw new ArgumentException("StartDate must be before EndDate");
    
    // Proceed with business logic
    return await _repository.ProcessAsync(request, cancellationToken);
}

// ✗ Bad: No validation, logic proceeds with bad data
public async Task<User> ProcessUserAsync(UserRequest request, CancellationToken cancellationToken)
{
    return await _repository.ProcessAsync(request, cancellationToken);
}
```

### Exception Handling Strategy

- **Throw specific exceptions** for validation/logic errors: `ArgumentException`, `ArgumentNullException`, `InvalidOperationException`
- **Log context** before re-throwing or returning error responses
- **Let exceptions bubble** from services to controllers; controllers decide response codes
- **Return typed `ProblemDetails`** from API endpoints for error responses

```csharp
// Controller handles service exceptions and converts to HTTP responses
[HttpPost("users")]
[ProducesResponseType(typeof(User), StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public async Task<ActionResult<User>> CreateUserAsync(
    [FromBody] UserRequest request,
    CancellationToken cancellationToken)
{
    try
    {
        var user = await _service.CreateUserAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetUserAsync), new { id = user.Id }, user);
    }
    catch (ArgumentException ex)
    {
        _logger.LogWarning(ex, "Invalid user request");
        return BadRequest(new ProblemDetails { Title = "Invalid Request", Detail = ex.Message });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating user");
        return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
    }
}
```

### API Response Typing

All API endpoints should return strongly-typed responses with documented status codes:

```csharp
[HttpGet("{id}")]
[ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public async Task<ActionResult<UserResponse>> GetUserAsync(
    string id,
    CancellationToken cancellationToken)
{
    var user = await _service.GetUserAsync(id, cancellationToken);
    if (user == null)
        return NotFound();
    
    return Ok(user);
}
```

### Data Access & Repository Pattern

Repositories should focus on data access, not business logic:

```csharp
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public class UserRepository : IUserRepository
{
    private readonly DbContext _context;
    private readonly ILogger<UserRepository> _logger;
    
    public UserRepository(DbContext context, ILogger<UserRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }
    
    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

### AutoMapper Profiles & DTO Mapping

Keep mappings simple; move complex logic to services:

```csharp
public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<UserRequest, User>();
        CreateMap<User, UserResponse>();
        
        // For nested mappings, keep them simple
        CreateMap<User, UserDetailsResponse>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}"));
    }
}

// Register in Program.cs
builder.Services.AddAutoMapper(typeof(UserProfile));

// Use in services
var userResponse = _mapper.Map<UserResponse>(user);
```

---

## Testing Overview

This repository uses **xUnit** for testing. For detailed unit test guidance, see the dedicated **Unit Test Agent** (when available).

### Test Project Naming & Location

- **Naming**: `[ProjectName].Tests` (e.g., `Foundation.Common.DataGenerator.Tests`)
- **Organization**: Mirror production namespace structure
- **Pattern**: Arrange-Act-Assert (AAA)

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests in specific project
dotnet test Foundation.Common.DataGenerator.Tests

# Run tests matching a pattern
dotnet test --filter "MethodName=CreateUserAsync_WhenValid_ReturnsUser"

# Run with coverage
dotnet test /p:CollectCoverage=true
```

### High-Level Test Guidance

✅ **AI agents should:**
- Test one behavior per test method
- Use clear, descriptive names: `CreateUserAsync_WhenValid_ReturnsUser`
- Mock external dependencies (repositories, external services, Azure clients)
- Test both success and error paths
- Validate meaningful assertions (not just null checks)

❌ **AI agents should avoid:**
- Testing implementation details (private methods, internal state)
- Shared state between tests
- Unobserved async tasks in tests
- Tests with multiple unrelated concerns

---

## Patterns AI Should Follow

### 1. Configuration Management

Use `IConfiguration` and environment-based settings; never hardcode:

```csharp
// ✓ Good: Load from configuration
public class AzureClientFactory
{
    private readonly IConfiguration _config;
    
    public AzureClientFactory(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }
    
    public SomeClient CreateClient()
    {
        var connectionString = _config["Azure:ConnectionString"]
            ?? throw new InvalidOperationException("Azure:ConnectionString is not configured");
        
        return new SomeClient(connectionString);
    }
}

// In Program.cs
builder.Configuration.AddAzureAppConfiguration(options =>
    options.Connect(settings["AppConfig:ConnectionString"]));
```

### 2. Health Checks & Readiness

Include health checks for dependencies when building APIs:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddCheck<AzureConnectivityCheck>("azure");

app.MapHealthChecks("/health");
```

### 3. Nullable Types & Null Safety

Always fix nullability warnings; don't suppress them:

```csharp
// ✓ Good: Explicit null handling
public User? GetUserIfExists(string id)
{
    var user = _repository.GetById(id);
    return user != null ? _mapper.Map<User>(user) : null;
}

// Or: Guard against null
public User GetUser(string id)
{
    var user = _repository.GetById(id)
        ?? throw new InvalidOperationException($"User {id} not found");
    return _mapper.Map<User>(user);
}

// ✗ Bad: Suppressions
#pragma warning disable CS8602
var name = GetNullableString().ToUpper();
#pragma warning restore CS8602
```

### 4. Minimal API Conventions (if using Minimal APIs)

If not using controllers, follow this pattern:

```csharp
app.MapGet("/api/users/{id}", GetUserAsync)
    .Produces<UserResponse>(StatusCodes.Status200OK)
    .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
    .WithName("GetUser")
    .WithOpenApi();

async Task<IResult> GetUserAsync(string id, IUserService service, CancellationToken ct)
{
    var user = await service.GetUserAsync(id, ct);
    return user != null ? Results.Ok(user) : Results.NotFound();
}
```

---

## Patterns AI Should AVOID

### ❌ Critical Anti-Patterns

#### 1. Sync-Over-Async (Blocking Calls)
```csharp
// ✗ Critical: Causes deadlocks and thread starvation
var result = _service.GetUserAsync(id).Result;
var data = task.GetAwaiter().GetResult();

// ✓ Good
var result = await _service.GetUserAsync(id, cancellationToken);
```

#### 2. Unobserved Async Tasks
```csharp
// ✗ Bad: Exception swallowed, behavior undefined
_service.ProcessAsync(data); // No await

// ✓ Good
await _service.ProcessAsync(data, cancellationToken);

// Or: If intentionally async, use BackgroundService
public class BackgroundWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _service.ProcessAsync(data, stoppingToken);
    }
}
```

#### 3. Service Locator Pattern
```csharp
// ✗ Bad: Hides dependencies, hard to test, breaks DI
public class Service
{
    public async Task DoWork()
    {
        var repo = ServiceLocator.GetService<IRepository>();
        await repo.GetAsync();
    }
}

// ✓ Good: Constructor injection
public class Service
{
    private readonly IRepository _repository;
    
    public Service(IRepository repository)
    {
        _repository = repository;
    }
    
    public async Task DoWork()
    {
        await _repository.GetAsync();
    }
}
```

#### 4. Mixing Concerns Across Layers
```csharp
// ✗ Bad: Business logic in controller
[HttpPost("users")]
public async Task<IActionResult> CreateUserAsync([FromBody] UserRequest request)
{
    if (request.Age < 18)
        return BadRequest("Must be 18+");
    
    // More validation, business rules, etc.
    var user = await _repository.AddAsync(request);
    return Ok(user);
}

// ✓ Good: Business logic in service
[HttpPost("users")]
public async Task<ActionResult<User>> CreateUserAsync([FromBody] UserRequest request, CancellationToken ct)
{
    var user = await _service.CreateUserAsync(request, ct);
    return CreatedAtAction(nameof(GetUserAsync), new { id = user.Id }, user);
}

// Service handles validation and business rules
public async Task<User> CreateUserAsync(UserRequest request, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(request);
    
    if (request.Age < 18)
        throw new ArgumentException("Must be 18+");
    
    return await _repository.AddAsync(_mapper.Map<User>(request), ct);
}
```

#### 5. Silent Exception Swallowing
```csharp
// ✗ Bad: Exception is lost
try
{
    var result = await _service.GetAsync(id, ct);
}
catch { } // Silent catch!

// ✓ Good: Log and handle appropriately
try
{
    var result = await _service.GetAsync(id, ct);
    return Ok(result);
}
catch (NotFoundException ex)
{
    _logger.LogWarning(ex, "Resource not found for id {Id}", id);
    return NotFound();
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error");
    return StatusCode(500, new ProblemDetails { Title = "Internal Server Error" });
}
```

#### 6. Hardcoded Configuration
```csharp
// ✗ Bad: Environment-specific magic strings
var connStr = "Server=hardcoded-prod;Database=prod";

if (Environment.GetEnvironmentVariable("ENV") == "Dev")
    connStr = "Server=localhost;Database=dev";

// ✓ Good: Use IConfiguration
var connStr = _configuration["Database:ConnectionString"]
    ?? throw new InvalidOperationException("Database:ConnectionString is required");
```

#### 7. Ignoring Nullability Warnings
```csharp
// ✗ Bad: Warnings suppressed
#pragma warning disable CS8602, CS8604
var upper = GetNullableString().ToUpper();
#pragma warning restore CS8602, CS8604

// ✓ Good: Handle nullability explicitly
var str = GetNullableString();
var upper = str?.ToUpper() ?? string.Empty;
```

---

## Common Development Workflows

### Adding a New Feature (Service & API Endpoint)

1. **Define contracts** in `[Domain].Contracts`: Request/Response DTOs, enums
2. **Create/update entities** in `[Domain].Entities`: Domain models
3. **Update repository interface** in `[Domain].Repository`: Add data access methods
4. **Implement repository methods** with async patterns and cancellation support
5. **Create/update service** in `[Domain].Service`: Business logic and orchestration
6. **Add controller endpoint** in `[API].Controllers`: HTTP routing and error handling
7. **Configure mapping** in `[Domain].Automapper.Profiles`: DTO transformations
8. **Register services** in `Program.cs`: Add DI registrations if new
9. **Add/update tests**: Verify behavior and error cases
10. **Validate**: `dotnet build && dotnet test`

### Updating an Entity Model

1. Modify entity class in `[Domain].Entities`
2. Create EF Core migration: `dotnet ef migrations add [MigrationName] --project [Domain].DAL --startup-project [API]`
3. Review migration file for correctness
4. Update repository methods if needed
5. Update corresponding DTOs in contracts
6. Update AutoMapper profiles
7. Update/add service logic if needed
8. Update tests
9. `dotnet build && dotnet test`

### Refactoring Existing Code

1. Run tests before changes to establish baseline: `dotnet test`
2. Make focused changes (one concern at a time)
3. Run tests after changes to verify nothing broke: `dotnet test`
4. Use compiler and nullability warnings as guides
5. Commit with clear message explaining rationale
6. Leave code cleaner than found (Boy Scout Rule)

---

## Azure Integration Patterns

### Service Registration & Configuration

```csharp
// In Program.cs
var config = builder.Configuration;

// Azure Data Explorer (Kusto)
if (!builder.Environment.IsDevelopment())
{
    var kustoCluster = config["Kusto:ClusterUri"]
        ?? throw new InvalidOperationException("Kusto:ClusterUri required");
    builder.Services.AddSingleton(new KustoConnection(kustoCluster));
}

// Azure Cosmos DB
var cosmosConnection = config["Cosmos:ConnectionString"]
    ?? throw new InvalidOperationException("Cosmos:ConnectionString required");
builder.Services.AddSingleton(new CosmosClient(cosmosConnection));

// Event Hubs (for event-driven scenarios)
var eventHubsConnection = config["EventHubs:ConnectionString"];
if (!string.IsNullOrEmpty(eventHubsConnection))
{
    builder.Services.AddScoped<IEventPublisher, EventHubsPublisher>();
}

// Application Insights (auto-instrumentation)
builder.Services.AddApplicationInsightsTelemetry();
```

### DefaultAzureCredential Pattern

Use managed identities in Azure, dev credentials locally:

```csharp
// ✓ Good: Works in both Azure and local dev
var credential = new DefaultAzureCredential();

// Azure Data Explorer
var kcsb = new KustoConnectionStringBuilder(clusterUri)
    .WithAadAzureTokenCredentialsAuthentication(credential);
var provider = KustoClientFactory.CreateCslQueryProvider(kcsb);

// Cosmos DB
var client = new CosmosClient(endpoint, credential);

// Event Hubs
var client = new EventHubProducerClient(fullyQualifiedNamespace, credential);
```

### Observability & Logging

Application Insights auto-collects HTTP requests and dependencies; use structured logging for business context:

```csharp
// Structured logging for business context
_logger.LogInformation(
    "Processing batch {BatchId} for user {UserId} with {RecordCount} records",
    batchId, userId, records.Count);

// Application Insights tracks:
// - HTTP requests/responses
// - Dependency calls (SQL, HTTP, etc.)
// - Exceptions
// - Performance metrics

// Custom telemetry when needed
_telemetryClient.TrackEvent("DataProcessingCompleted",
    new Dictionary<string, string> { { "BatchId", batchId } },
    new Dictionary<string, double> { { "RecordCount", records.Count } });
```

---

## Build & Validation Commands

```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build Foundation.Audit.Service

# Restore packages
dotnet restore

# Run all tests
dotnet test

# Run tests with coverage
dotnet test /p:CollectCoverage=true /p:CoverageOutputFormat=opencover

# Create EF migration
dotnet ef migrations add [MigrationName] --project [Domain].DAL --startup-project [API]

# Update database
dotnet ef database update --project [Domain].DAL --startup-project [API]

# Clean and rebuild
dotnet clean && dotnet build

# List available projects
dotnet sln list
```

---

## When to Ask for Clarification

Even with these guidelines, AI agents should ask for clarification when:

1. **Ambiguous requirements**: "Should this be a new service or extend an existing one?"
2. **Design conflicts**: "This pattern would violate layer separation; should we adjust?"
3. **Performance tradeoffs**: "Should we paginate large result sets or stream them?"
4. **Error handling**: "Should this endpoint return 404 or 400 for invalid input?"
5. **Azure service choice**: "Should this use Event Hubs or Service Bus?"
6. **Nullability uncertainty**: "Can this property be null in production scenarios?"
7. **Cross-domain dependencies**: "Does this contract belong in Foundation.Common or domain-specific?"

---

## Key References

- **Full project guidelines**: See `.github/copilot-instructions.md` for detailed standards and examples
- **Target Framework**: .NET 10, C# 14
- **Architecture**: Domain-driven, layered by function
- **Testing**: xUnit (see dedicated Unit Test Agent for detailed guidance)
- **Azure Services**: Multiple services integrated; see Azure patterns above

---

**Last Updated**:   
**Repository**: 
