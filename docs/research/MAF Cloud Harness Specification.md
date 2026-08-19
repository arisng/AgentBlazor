# **MAF Cloud Harness Specification**

## **Executive Architectural Context**

This document defines the comprehensive architecture and functional specification for building a cloud-native agent harness using the Microsoft Agent Framework (MAF / Microsoft.Agents.AI) in .NET.

Unlike desktop developer tools (e.g., GitHub Copilot CLI), this cloud harness operates in a completely **disembodied, server-side multi-tenant environment**. Workspaces, custom prompts, workflows, and agent skills are **never** sourced from local Git repositories or local host OS filesystems. Instead, the canonical source of truth for all code assets, state, user files, and skills is **Blob Storage** (e.g., Azure Blob Storage, AWS S3, or S3-compatible object stores).

Each active chat turn executes within a multi-tenant isolation boundary partitioned as:

* **Workspace Scope:** tenants/{tenantId}/users/{userId}/sessions/{sessionId}/workspace/  
* **Snapshots Scope:** tenants/{tenantId}/users/{user_id}/sessions/{session_id}/snapshots/{snapshot_id}/  
* **Tenant Custom Skills:** tenants/{tenantId}/skills/{skillId}/  
* **Session Custom Skills:** tenants/{tenantId}/users/{userId}/sessions/{sessionId}/skills/{skillId}/

┌─────────────────────────────────────────────────────────────────────────────────────────────────┐  
│                                       BLOB STORAGE                                              │  
│   Canonical Store for Workspace Files, Session State, Snapshots, and Multi-Tier Agent Skills    │  
│   ├── tenants/{tenantId}/skills/ (Tenant-Scoped Skills)                                         │  
│   └── tenants/{tenantId}/users/{userId}/sessions/{sessionId}/                                  │  
│       ├── workspace/ (User Workspace Files)                                                     │  
│       └── skills/    (Session-Scoped Skills)                                                   │  
└───────────────────────────────────────────────┬─────────────────────────────────────────────────┘  
                                                │ Hydrate / Flush Streams  
                                                ▼  
┌─────────────────────────────────────────────────────────────────────────────────────────────────┐  
│                          SESSION WORKSPACE & SKILL REGISTRY ENGINE                              │  
│   Ephemeral Memory / SSD Storage Tiering (Isolated per User-Chat-Session)                      │  
│   ├── Workspace Metadata Manifest & Index     ├── Skill Manifest Index & Blob Resolver          │  
│   ├── VFS Dirty State Tracker                 └── In-Memory / Sandboxed Skill Sandbox           │  
└───────────────────────────────────────────────┬─────────────────────────────────────────────────┘  
                                                │ Exposes IVirtualFileSystem & ISkillRegistry  
                                                ▼  
┌─────────────────────────────────────────────────────────────────────────────────────────────────┐  
│                            MAF CONTEXT & TOKEN BUDGET MANAGER                                   │  
│   ├── Hard Quota Allocator (System / Tools / Context / History / Buffer)                        │  
│   ├── Rolling Auto-Compaction Pipeline                                                          │  
│   └── Large Tool Output Offloading & Pointer Masking                                            │  
└───────────────────────────────────────────────┬─────────────────────────────────────────────────┘  
                                                │ Prepares Context Payload  
                                                ▼  
┌─────────────────────────────────────────────────────────────────────────────────────────────────┐  
│                              MAF AIAGENT EXECUTION PIPELINE                                     │  
│   ├── Pre-Execution Approval Middleware (Human-In-The-Loop Safety Gate)                         │  
│   ├── Core AIAgent Loop & State Machine (Plan Mode vs Execute Mode)                             │  
│   └── Bound AIFunction Tools (VFS Tools, Skill Execution Tools, Search Index Tools)             │  
└─────────────────────────────────────────────────────────────────────────────────────────────────┘

## **Part 1: Comprehensive Subsystem Specifications**

### **Subsystem 1: Session Virtual File System (VFS) & Blob Persistence**

* **Deterministic Path Partitioning:** Enforce strict blob key naming conventions:  
  * Root Workspace: tenants/{tenant_id}/users/{user_id}/sessions/{session_id}/workspace/  
  * Workspace Snapshots: tenants/{tenant_id}/users/{user_id}/sessions/{session_id}/snapshots/{snapshot_id}/  
* **Session Initialization & Fast Manifest Scanning:**  
  * On session start, perform an asynchronous prefix scan of Blob Storage to construct an in-memory WorkspaceManifest (file relative paths, sizes, ETag hashes, last modified dates) without downloading file byte contents.  
* **Lazy Hydration & Tiered Ephemeral Storage:**  
  * Download individual file contents on-demand into ephemeral session storage when accessed by read_file, grep, or apply_patch.  
  * Store small files (< 1MB) directly in memory (MemoryStream / UTF-8 byte arrays).  
  * Offload large files (> 1MB) to session-isolated disk locations (e.g., /tmp/maf_workspaces/{tenantId}/{sessionId}/).  
* **Dirty State State Machine:** Maintain state tracking for every file entry in the session workspace:  
  * Clean: Unmodified relative to Blob Storage.  
  * Modified: Content altered in session cache.  
  * Created: New file generated during agent turn.  
  * Deleted: Marked for deletion.  
* **Large File Operations & High-Efficiency Patching Tools:**  
  * read_file_range: Stream or read specific line bounds (e.g., lines 120–210) without loading multi-megabyte files into the LLM context.  
  * apply_patch: Perform anchor-based search-and-replace block matching (SEARCH block vs REPLACE block) against current VFS file state before committing edits.  
  * edit_file_range: Replace specific line ranges directly in the VFS stream.  
* **Post-Turn Delta Sync-Back:**  
  * Intercept end-of-turn execution to scan dirty files.  
  * Stream updated files back to Blob Storage in parallel using concurrent uploads and ETag optimistic concurrency checks.

### **Subsystem 2: Blob-Native Agent Skills Engine**

* **Strict Non-Filesystem Sourcing:** Guarantee skills are **never** read from host disk paths or local repository folders (e.g., .github/). All skill packages must be parsed directly from Blob Storage streams.  
* **Standardized Blob Skill Package Layout:**  
  * Metadata & Prompts: SKILL.md (or skill.json)  
  * Executable Scripts: scripts/  
  * Asset Templates: templates/  
* **Multi-Tier Skill Hierarchy:**  
  * **Global System Skills:** Immutable system-wide skill packages embedded or stored in core system blob containers.  
  * **Tenant-Scoped Skills:** Shared organizational workflows at tenants/{tenantId}/skills/{skillId}/.  
  * **Session-Scoped Skills:** Dynamic skills created during user chat sessions at tenants/{tenantId}/users/{userId}/sessions/{sessionId}/skills/{skillId}/.  
* **Zero-Disk Discovery Indexing:**  
  * Scan blob prefixes to parse SKILL.md header manifests (Name, Description, Category, Target Context) into an in-memory SkillCatalogIndex.  
  * Inject only compact skill summary listings into system instructions to protect the token budget.  
* **Dynamic Skill Activation (activate_skill):**  
  * Expose an activate_skill(skill_id) tool to the agent.  
  * On activation, hydrate skill prompt overlays, dynamic tools, and assets directly into the active session loop context.  
* **Sandboxed Script Execution:**  
  * Execute skill scripts (Python, Node.js, PowerShell) within restricted WebAssembly, containerized runners, or virtual processes.  
  * Bind inputs and outputs strictly to the session IVirtualFileSystem.

### **Subsystem 3: Context Engineering, Token Budgeting & Auto-Compaction**

* **Hard Token Quota Allocator:** Enforce strict context budget management partitioning the model's total context window (e.g., 128k/200k) into bounded quotas:

| Context Category | Quota Allocation (128k Window) | Hard/Soft Ceiling | Overflow & Eviction Strategy |
| :---- | :---- | :---- | :---- |
| **System Persona & Rules** | 8–10% (~10,000 tokens) | Hard Ceiling | Fixed. Never evicted or truncated. |
| **Tool Schemas & Active Skills** | 10–12% (~15,000 tokens) | Soft Ceiling | Filter tool schemas based on active plan step; drop inactive skills. |
| **Workspace Context / AST** | 30–35% (~45,000 tokens) | Hard Ceiling | FIFO cache or AST symbol relevance ranking. |
| **Conversation & Execution History** | 30–35% (~38,000 tokens) | Soft Ceiling | Background sub-agent compaction when soft ceiling is hit. |
| **Free Output Buffer** | 15–20% (~20,000 tokens) | Reserved | Reserved buffer for model thinking tokens and structured tool calls. |

* **Dynamic Pre-Turn Context Assembly:** Assemble system instructions, active tools, filtered workspace trees, and compacted history prior to each model call.  
* **Rolling Auto-Compaction Pipeline:**  
  * Trigger when conversation history breaches its 30% soft ceiling.  
  * Run a lightweight sub-agent pass to summarize completed sub-tasks into a "Current State Summary" block.  
  * Retain critical unresolved errors and feedback while replacing turn history pairs.  
* **Large Tool Output Offloading & Pointer Masking:**  
  * Intercept tool returns exceeding a threshold (e.g., > 1,500 tokens).  
  * Offload raw stdout/stderr logs or large payloads to Blob/Redis storage.  
  * Inject a reference pointer (\[Output truncated. Ref ID: #blob-9021]) with key extracted symbols into the chat history.

### **Subsystem 4: Agent Execution Loop, Planning & Guardrails**

* **Operating Mode State Machine:** Implement mode tracking (e.g., **Plan Mode** vs. **Execute Mode**) to enforce upfront task decomposition before tool invocation.  
* **Persistent Todo & Step Tracker (ITodoProvider):** Maintain active steps, completed milestones, and block dependencies across model tool turns.  
* **Loop Guardrails & Stall Detection:**  
  * Configure hard iteration limits to prevent runaway tool loops.  
  * Detect repetitive identical tool calls or cyclic error loops and force plan revision.

### **Subsystem 5: Multi-Tenancy, Safety, Governance & Observability**

* **Pre-Execution Approval Middleware:** Intercept destructive or high-cost tool calls (e.g., shell command execution, file deletions, external API mutations) before execution.  
* **Path Traversal Prevention:** Sanitize all VFS paths (blocking ../, absolute paths /etc/passwd, or null bytes) at the VFS abstraction level before resolving against Blob Storage or ephemeral storage.  
* **Storage Quota Enforcement:** Enforce max total workspace size limit per session (e.g., 500MB) and max single-file size limit (e.g., 20MB).  
* **OpenTelemetry Integration:** Emit standard GenAI traces covering model calls, latency, tool invocation timings, and compaction loops.  
* **Token Cost Accounting:** Capture prompt, completion, and tool usage metrics tagged by tenant_id, user_id, session_id, and agent_mode.  
* **Stateful Session Checkpointing & Resumption:** Persist session history, VFS dirty state, and todo status to durable storage after every turn to enable mid-run crash recovery.

## **Part 2: Proposed C# Interface Contracts**

These interfaces represent the foundational contracts to be implemented in .NET for MAF integration.

```csharp
namespace MAF.Harness.Core;

// ==========================================  
// 1. VFS & WORKSPACE CONTRACTS  
// ==========================================

public enum FileSyncState  
{  
    Clean,  
    Modified,  
    Created,  
    Deleted  
}

public record WorkspaceFileEntry(  
    string RelativePath,  
    long SizeInBytes,  
    string ContentHash,  
    DateTimeOffset LastModified,  
    FileSyncState State  
);

public record PatchResult(  
    bool Success,  
    string RelativePath,  
    int LinesModified,  
    string? ErrorMessage  
);

public interface IVirtualFileSystem  
{  
    string TenantId { get; }  
    string UserId { get; }  
    string SessionId { get; }

    Task<IReadOnlyList<WorkspaceFileEntry>> ListFilesAsync(string relativePath = "", string searchPattern = "*", CancellationToken ct = default);  
    Task<bool> FileExistsAsync(string relativePath, CancellationToken ct = default);  
      
    Task<string> ReadFileAsync(string relativePath, CancellationToken ct = default);  
    Task<string> ReadFileRangeAsync(string relativePath, int startLine, int endLine, CancellationToken ct = default);  
      
    Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default);  
    Task<PatchResult> ApplyPatchAsync(string relativePath, string searchBlock, string replaceBlock, CancellationToken ct = default);  
    Task<PatchResult> EditLineRangeAsync(string relativePath, int startLine, int endLine, string newContent, CancellationToken ct = default);  
      
    Task DeleteFileAsync(string relativePath, CancellationToken ct = default);  
    Task<IReadOnlyList<WorkspaceFileEntry>> GetDirtyFilesAsync(CancellationToken ct = default);  
}

public interface ISessionWorkspaceManager  
{  
    Task<IVirtualFileSystem> InitializeSessionWorkspaceAsync(string tenantId, string userId, string sessionId, CancellationToken ct = default);  
    Task FlushDirtyFilesToBlobAsync(IVirtualFileSystem vfs, CancellationToken ct = default);  
    Task<string> CreateSnapshotAsync(IVirtualFileSystem vfs, string snapshotLabel, CancellationToken ct = default);  
    Task PurgeLocalSessionCacheAsync(string tenantId, string userId, string sessionId, CancellationToken ct = default);  
}

// ==========================================  
// 2. AGENT SKILLS CONTRACTS  
// ==========================================

public enum SkillScope  
{  
    GlobalSystem,  
    Tenant,  
    SessionUser  
}

public record AgentSkillManifest(  
    string SkillId,  
    string Name,  
    string Description,  
    string Category,  
    SkillScope Scope,  
    string BlobStoragePrefix,  
    IReadOnlyList<string> RequiredTools,  
    IReadOnlyDictionary<string, string> EnvironmentVariables,  
    string SystemInstructionsOverlay  
);

public record SkillExecutionResult(  
    bool Success,  
    string OutputText,  
    IReadOnlyList<string> ModifiedVfsPaths,  
    string? ErrorDetails  
);

public interface ISkillRegistry  
{  
    Task<IReadOnlyList<AgentSkillManifest>> DiscoverAvailableSkillsAsync(string tenantId, string userId, string sessionId, CancellationToken ct = default);  
    Task<AgentSkillManifest?> HydrateSkillManifestAsync(string tenantId, string userId, string sessionId, string skillId, CancellationToken ct = default);  
    Task<SkillExecutionResult> ExecuteSkillScriptAsync(string skillId, string scriptName, IDictionary<string, object> parameters, IVirtualFileSystem vfs, CancellationToken ct = default);  
}

// ==========================================  
// 3. CONTEXT & TOKEN BUDGET CONTRACTS  
// ==========================================

public record TokenBudgetAllocation(  
    int TotalWindow,  
    int SystemQuota,  
    int ToolSchemaQuota,  
    int WorkspaceContextQuota,  
    int ConversationHistoryQuota,  
    int OutputBufferQuota  
);

public record CompactionResult(  
    bool Compacted,  
    int OriginalTokenCount,  
    int NewTokenCount,  
    string CompactedHistorySummary  
);

public interface ITokenBudgetManager  
{  
    TokenBudgetAllocation CurrentAllocation { get; }  
    int CalculateTokenCount(string text);  
    Task<bool> ShouldCompactAsync(int currentHistoryTokenCount);  
    Task<CompactionResult> CompactHistoryAsync(IReadOnlyList<object> chatHistory, CancellationToken ct = default);  
    string MaskLargeOutput(string rawToolOutput, out string blobReferenceId);  
}
```

## **Part 3: Target Directives for Deep Research Agent**

1. **Hydration Strategy Trade-offs & Hybrid Caching:**  
   * Compare On-Demand Lazy Hydration vs. Full Ephemeral Sync for typical repository file distributions (e.g., 5,000 text files totaling 30MB).  
   * Evaluate predictive pre-fetching strategies (e.g., automatically downloading imported/referenced modules when an entry file is opened).  
2. **Optimistic Concurrency & ETag Synchronization:**  
   * Evaluate ETag validation strategies during FlushDirtyFilesToBlobAsync to prevent overwrite race conditions if multiple agent loops or user API requests occur concurrently.  
   * Define fallback resolution strategies when a blob has been modified by an external process during an active agent session turn.  
3. **High-Performance Diff/Patch Engines in .NET:**  
   * Investigate C# implementations (such as DiffPlex, custom AST-aware line matchers, or fuzzy string matchers) for high-reliability SEARCH/REPLACE block matching in apply_patch.  
   * Formulate error handling mechanisms for handling whitespace variations, trailing newlines, or minor indentation shifts during LLM-generated patch application.  
4. **Integration with MAF AIFunction & Request Context Binding:**  
   * Design the dependency injection and wrapper strategy to automatically bind IVirtualFileSystem and ISkillRegistry from the active HTTP/gRPC request context to MAF agent tool executions.  
   * Establish lifetime scoping rules (Scoped vs Transient) for IVirtualFileSystem within ASP.NET Core / MAF agent execution pipelines.  
5. **Blob-Native Skill Discovery & Dynamic Tool Routing:**  
   * Research methods for dynamically attaching and detaching MAF AIFunction tools when an agent activates or deactivates a skill loaded from Blob Storage mid-session.  
   * Evaluate semantic vs. keyword matching for discovering skills in Blob Storage without exceeding system prompt token quotas.  
6. **Memory Management, GC Pressure & Ephemeral Scrubbing:**  
   * Analyze streaming memory management strategies (RecyclableMemoryStream) to minimize LOH (Large Object Heap) allocations during multi-megabyte file edits.  
   * Design deterministic teardown hooks to purge local storage and release unmanaged resource locks immediately upon session completion or timeout.