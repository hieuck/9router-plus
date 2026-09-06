# Thread Safety Guidelines

**Last Updated:** 2026-09-06

## ViewModels - UI Thread Assumption

All ViewModels in this application assume **single-threaded access on the UI thread** unless explicitly documented otherwise.

### Why This Is Safe

WPF ViewModels are bound to UI controls via data binding. WPF's binding system marshals property changes to the UI thread automatically via `INotifyPropertyChanged`. User interactions (button clicks, input changes) also occur on the UI thread.

**Implication:** ViewModels do not need internal locks for property access or command execution because WPF guarantees single-threaded access.

### ViewModels Following This Pattern

- `MainViewModel`
- `GoogleAutoLoginViewModel`
- `CredentialsManagerViewModel`
- `DiagnosticsViewModel`
- `ProfileRowViewModel`
- All other ViewModels in `src/RouterPlus.App/ViewModels/`

### Exceptions - Thread-Safe Components

The following components ARE thread-safe and can be accessed from any thread:

#### Vault Stores (Thread-Safe)
- `GoogleAccountVaultStore` - Uses `VaultStoreBase` disposal/concurrency pattern
- `ProviderConnectionVaultStore` - Uses `VaultStoreBase` disposal/concurrency pattern
- `DpapiSecretVault` - Thread-safe encryption operations

**Safety mechanisms:**
- `SemaphoreSlim _writeLock` for exclusive write access
- `SemaphoreSlim _operationGate` for disposal coordination
- `object _disposalLock` for disposal state management
- Tested in `VaultStoreBaseTests` (12 tests covering concurrency)

#### Observability (Thread-Safe)
- `ObservabilityHub` - Singleton, thread-safe event logging
- Async file I/O with internal synchronization
- Tested in `ObservabilityE2ETests` (28 tests)

#### Chrome CDP Client (Thread-Safe)
- `ChromeCdpClient` - WebSocket communication is inherently async
- Uses `SemaphoreSlim` for request/response coordination

### Background Operations

When ViewModels perform background work (network calls, file I/O), they use:

```csharp
// Start background work
var result = await SomeAsyncOperation();

// Update UI properties (automatically marshaled to UI thread by WPF binding)
SomeProperty = result;
OnPropertyChanged(nameof(SomeProperty));
```

WPF's data binding handles the thread transition automatically via `Dispatcher`.

### When NOT to Add Locks

❌ **Don't add locks to ViewModels** for property access:
```csharp
// WRONG - Unnecessary lock
private readonly object _lock = new();
public string Name 
{ 
    get { lock(_lock) return _name; }
    set { lock(_lock) { _name = value; OnPropertyChanged(); } }
}
```

✅ **Correct - Rely on UI thread assumption:**
```csharp
public string Name 
{ 
    get => _name;
    set { _name = value; OnPropertyChanged(); }
}
```

### When TO Add Synchronization

✅ **Add synchronization when:**
1. Component is explicitly designed for multi-threaded access (vault stores, hubs)
2. Component maintains shared mutable state accessed from background threads
3. Component implements `IDisposable` and needs to coordinate disposal with operations

### Testing Thread Safety

Components claiming thread-safety should have tests covering:
- Concurrent read operations
- Concurrent write operations
- Disposal during operations
- Cancellation during operations

See `VaultStoreBaseTests.cs` for examples.

---

## Summary

| Component Type | Thread Safety | Mechanism |
|---|---|---|
| ViewModels | UI thread only | WPF binding + Dispatcher |
| Vault Stores | Thread-safe | SemaphoreSlim + disposal pattern |
| ObservabilityHub | Thread-safe | Internal synchronization |
| ChromeCdpClient | Thread-safe | Async + SemaphoreSlim |

**Default assumption:** UI thread only unless explicitly documented as thread-safe.
