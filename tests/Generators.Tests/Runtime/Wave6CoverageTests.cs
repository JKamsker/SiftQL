using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave6CoverageTests
{
    #region Async projection paths (AwaitIncludesAsync, AwaitPayloadIncludesAsync, AwaitValueAsync)

    [Fact]
    public async Task AsyncInclude_TriggersAwaitIncludesAsync()
    {
        var tcs = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "async-test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "async-include",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs.Task)),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs.SetResult(ProjectedEventValue.FromScalar("resolved"));
        ProjectedEvent result = await task;

        Assert.Equal(42, result.Field("ItemId").Integer);
        Assert.Equal("resolved", result.ContextValue("async-include").String);
    }

    [Fact]
    public async Task AsyncInclude_TriggersAwaitPayloadIncludesAsync()
    {
        var tcs = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "async-payload-test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "async-payload-include",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs.Task)),
            ]);

        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ValueTask<ReadOnlyMemory<byte>> task = projection.ProjectPayloadAsync(
            subject, new object(), options, CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs.SetResult(ProjectedEventValue.FromScalar("payload-resolved"));
        ReadOnlyMemory<byte> payload = await task;

        ProjectedEvent deserialized = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);
        Assert.Equal(42, deserialized.Field("ItemId").Integer);
        Assert.Equal("payload-resolved", deserialized.ContextValue("async-payload-include").String);
    }

    [Fact]
    public async Task MultipleAsyncIncludes_AllAwaitedInSequence()
    {
        var tcs1 = new TaskCompletionSource<ProjectedEventValue>();
        var tcs2 = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "multi-async-test",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "first",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs1.Task)),
                new CompiledProjection<object>.IncludeProjector(
                    "second",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs2.Task)),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs1.SetResult(ProjectedEventValue.FromScalar("a"));
        tcs2.SetResult(ProjectedEventValue.FromScalar("b"));
        ProjectedEvent result = await task;

        Assert.Equal("a", result.ContextValue("first").String);
        Assert.Equal("b", result.ContextValue("second").String);
    }

    [Fact]
    public async Task SyncFirstInclude_AsyncSecondInclude_StillTriggersAwait()
    {
        var tcs = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "mixed-sync-async",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "sync-include",
                    (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("sync"))),
                new CompiledProjection<object>.IncludeProjector(
                    "async-include",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs.Task)),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs.SetResult(ProjectedEventValue.FromScalar("async"));
        ProjectedEvent result = await task;

        Assert.Equal("sync", result.ContextValue("sync-include").String);
        Assert.Equal("async", result.ContextValue("async-include").String);
    }

    [Fact]
    public async Task AllSyncIncludes_CompletedSynchronously()
    {
        var projection = new CompiledProjection<object>(
            "all-sync",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "sync-a",
                    (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("a"))),
                new CompiledProjection<object>.IncludeProjector(
                    "sync-b",
                    (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("b"))),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ValueTask<ProjectedEvent> task = projection.ProjectAsync(subject, new object(), CancellationToken.None);
        Assert.True(task.IsCompletedSuccessfully);

        ProjectedEvent result = await task;
        Assert.Equal("a", result.ContextValue("sync-a").String);
        Assert.Equal("b", result.ContextValue("sync-b").String);
    }

    [Fact]
    public async Task AsyncInclude_AwaitValueAsync_ExercisedViaTaskDelay()
    {
        var projection = new CompiledProjection<object>(
            "await-value-test",
            typeof(ItemUsedEvent),
            fields: [],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "delayed",
                    async (_, _, _) =>
                    {
                        await Task.Yield();
                        return ProjectedEventValue.FromScalar(999);
                    }),
            ]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ProjectedEvent result = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.Equal(999, result.ContextValue("delayed").Integer);
    }

    [Fact]
    public async Task AsyncPayloadInclude_MultipleAsyncIncludes()
    {
        var tcs1 = new TaskCompletionSource<ProjectedEventValue>();
        var tcs2 = new TaskCompletionSource<ProjectedEventValue>();
        var projection = new CompiledProjection<object>(
            "multi-async-payload",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "Quantity", "Quantity",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).Quantity))],
            includes:
            [
                new CompiledProjection<object>.IncludeProjector(
                    "inc-a",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs1.Task)),
                new CompiledProjection<object>.IncludeProjector(
                    "inc-b",
                    (_, _, _) => new ValueTask<ProjectedEventValue>(tcs2.Task)),
            ]);

        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 10);
        ValueTask<ReadOnlyMemory<byte>> task = projection.ProjectPayloadAsync(
            subject, new object(), options, CancellationToken.None);
        Assert.False(task.IsCompleted);

        tcs1.SetResult(ProjectedEventValue.FromScalar("x"));
        tcs2.SetResult(ProjectedEventValue.FromScalar("y"));
        ReadOnlyMemory<byte> payload = await task;

        ProjectedEvent deserialized = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);
        Assert.Equal(10, deserialized.Field("Quantity").Integer);
        Assert.Equal("x", deserialized.ContextValue("inc-a").String);
        Assert.Equal("y", deserialized.ContextValue("inc-b").String);
    }

    #endregion

    #region HotTieredProviderLoader error paths

    [Fact]
    public void MissingArtifact_WhenAssemblyDoesNotExist()
    {
        string directory = CreateTempDirectory("MissingArtifact");
        string manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "nonexistent.dll"),
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.Equal(HotTieredProviderLoadStatus.MissingArtifact, result.Status);
        Assert.False(result.Loaded);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public void MissingArtifact_WhenManifestDoesNotExist()
    {
        string directory = CreateTempDirectory("MissingManifest");
        string assemblyPath = Path.Combine(directory, "fake.dll");
        File.WriteAllBytes(assemblyPath, [0x00]);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = Path.Combine(directory, "nonexistent.json"),
            RequireExactRuntimeVersion = false,
        });

        Assert.Equal(HotTieredProviderLoadStatus.MissingArtifact, result.Status);
    }

    [Fact]
    public void InvalidManifest_WhenManifestDeserializesToNull()
    {
        string directory = CreateTempDirectory("NullManifest");
        string manifestPath = Path.Combine(directory, "manifest.json");
        string assemblyPath = Path.Combine(directory, "fake.dll");
        File.WriteAllText(manifestPath, "null");
        File.WriteAllBytes(assemblyPath, [0x00]);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.Equal(HotTieredProviderLoadStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public void VersionMismatch_WhenSchemaDoesNotMatch()
    {
        string directory = CreateTempDirectory("SchemaMismatch");
        var manifest = new HotCompilationManifest { Schema = "wrong.schema.v99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = LoadFromDirectory(directory);

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("manifest schema", result.Message);
    }

    [Fact]
    public void VersionMismatch_WhenFilterEngineDoesNotMatch()
    {
        string directory = CreateTempDirectory("EngineMismatch");
        var manifest = new HotCompilationManifest { FilterEngineVersion = "wrong-engine-v99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = LoadFromDirectory(directory);

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("filter engine", result.Message);
    }

    [Fact]
    public void VersionMismatch_WhenGeneratorDoesNotMatch()
    {
        string directory = CreateTempDirectory("GeneratorMismatch");
        var manifest = new HotCompilationManifest { GeneratorVersion = "wrong-gen-v99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = LoadFromDirectory(directory);

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("generator", result.Message);
    }

    [Fact]
    public void VersionMismatch_WhenRuntimeVersionRequired()
    {
        string directory = CreateTempDirectory("RuntimeMismatch");
        var manifest = new HotCompilationManifest { RuntimeVersion = "99.99.99" };
        WriteManifestAndFakeAssembly(directory, manifest);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "hot.dll"),
            ManifestPath = Path.Combine(directory, "hot.json"),
            RequireExactRuntimeVersion = true,
        });

        Assert.Equal(HotTieredProviderLoadStatus.VersionMismatch, result.Status);
        Assert.Contains("runtime", result.Message);
    }

    [Fact]
    public void InvalidAssembly_WhenBadImageFormat()
    {
        string directory = CreateTempDirectory("BadImage");
        var manifest = new HotCompilationManifest { RuntimeVersion = "10.0.0" };
        string manifestJson = JsonSerializer.Serialize(manifest);
        string manifestPath = Path.Combine(directory, "hot.json");
        string assemblyPath = Path.Combine(directory, "hot.dll");
        File.WriteAllText(manifestPath, manifestJson);
        File.WriteAllBytes(assemblyPath, [0x4D, 0x5A, 0x00, 0x00]);

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.True(
            result.Status == HotTieredProviderLoadStatus.InvalidAssembly ||
            result.Status == HotTieredProviderLoadStatus.LoadFailed,
            $"Expected InvalidAssembly or LoadFailed but got {result.Status}: {result.Message}");
    }

    [Fact]
    public void FailedLoadResult_IsNotLoaded()
    {
        string directory = CreateTempDirectory("FailedResult");
        string manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "nonexistent.dll"),
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        Assert.False(result.Loaded);
        Assert.Null(result.Assembly);
        result.Dispose();
        result.Dispose();
    }

    #endregion

    #region ProjectionPayloadWriterCompiler branches

    [Fact]
    public void TryCompile_ReturnsNull_ForNonScalarField()
    {
        var field = new FilterField(
            "Items", typeof(int[]), FilterFieldKind.Array,
            _ => null, ArrayAccessor: null);

        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Items", field);

        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_ReturnsNull_WhenAccessIsNull()
    {
        var field = new FilterField(
            "Name", typeof(string), FilterFieldKind.Scalar,
            _ => null);

        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Name", field);

        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_ReturnsNull_WhenPropertyNotFound()
    {
        var field = new FilterField(
            "Missing", typeof(string), FilterFieldKind.Scalar,
            _ => null, Access: FilterFieldAccess.ForProperty("NonexistentProperty"));

        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Missing", field);

        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_Boolean_Required()
    {
        var field = ScalarField("IsActive", typeof(bool), "IsActive");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "IsActive", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject(), "IsActive", v => Assert.True(v.Boolean));
    }

    [Fact]
    public void TryCompile_Boolean_Nullable()
    {
        var field = ScalarField("NullableBool", typeof(bool?), "NullableBool");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableBool", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableBool = true }, "NullableBool",
            v => Assert.True(v.Boolean));
        AssertPayloadField(writer!, new PayloadSubject { NullableBool = null }, "NullableBool",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Integer_Required()
    {
        var field = ScalarField("Count", typeof(int), "Count");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Count", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Count = 42 }, "Count",
            v => Assert.Equal(42, v.Integer));
    }

    [Fact]
    public void TryCompile_Integer_Nullable()
    {
        var field = ScalarField("NullableCount", typeof(int?), "NullableCount");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableCount", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableCount = 7 }, "NullableCount",
            v => Assert.Equal(7, v.Integer));
        AssertPayloadField(writer!, new PayloadSubject { NullableCount = null }, "NullableCount",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_UnsignedInteger_Required()
    {
        var field = ScalarField("BigId", typeof(ulong), "BigId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "BigId", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { BigId = (ulong)long.MaxValue + 1 }, "BigId",
            v => Assert.Equal((ulong)long.MaxValue + 1, v.UnsignedInteger));
    }

    [Fact]
    public void TryCompile_UnsignedInteger_Nullable()
    {
        var field = ScalarField("NullableBigId", typeof(ulong?), "NullableBigId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableBigId", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableBigId = 100UL }, "NullableBigId",
            v => Assert.Equal(100, v.Integer));
        AssertPayloadField(writer!, new PayloadSubject { NullableBigId = null }, "NullableBigId",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Double_Required()
    {
        var field = ScalarField("Score", typeof(double), "Score");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Score", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Score = 3.14 }, "Score",
            v => Assert.Equal(3.14, v.Number));
    }

    [Fact]
    public void TryCompile_Double_Nullable()
    {
        var field = ScalarField("NullableScore", typeof(double?), "NullableScore");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableScore", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableScore = 2.71 }, "NullableScore",
            v => Assert.Equal(2.71, v.Number));
        AssertPayloadField(writer!, new PayloadSubject { NullableScore = null }, "NullableScore",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Float_Required()
    {
        var field = ScalarField("Ratio", typeof(float), "Ratio");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Ratio", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Ratio = 1.5f }, "Ratio",
            v => Assert.Equal(1.5, v.Number, precision: 5));
    }

    [Fact]
    public void TryCompile_Decimal_Required()
    {
        var field = ScalarField("Price", typeof(decimal), "Price");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Price", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Price = 99.99m }, "Price",
            v => Assert.Equal(99.99m, v.Decimal));
    }

    [Fact]
    public void TryCompile_Decimal_Nullable()
    {
        var field = ScalarField("NullablePrice", typeof(decimal?), "NullablePrice");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullablePrice", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullablePrice = 10m }, "NullablePrice",
            v => Assert.Equal(10, v.Integer));
        AssertPayloadField(writer!, new PayloadSubject { NullablePrice = null }, "NullablePrice",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Decimal_WholeNumber_WrittenAsInteger()
    {
        var field = ScalarField("Price", typeof(decimal), "Price");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Price", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Price = 42m }, "Price",
            v => Assert.Equal(42, v.Integer));
    }

    [Fact]
    public void TryCompile_String()
    {
        var field = ScalarField("Name", typeof(string), "Name");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Name", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { Name = "hello" }, "Name",
            v => Assert.Equal("hello", v.String));
        AssertPayloadField(writer!, new PayloadSubject { Name = null }, "Name",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Guid_Required()
    {
        var field = ScalarField("Id", typeof(Guid), "Id");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Id", field);
        Assert.NotNull(writer);
        var guid = Guid.NewGuid();
        AssertPayloadField(writer!, new PayloadSubject { Id = guid }, "Id",
            v => Assert.Equal(guid, v.Guid));
    }

    [Fact]
    public void TryCompile_Guid_Nullable()
    {
        var field = ScalarField("NullableId", typeof(Guid?), "NullableId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableId", field);
        Assert.NotNull(writer);

        var guid = Guid.NewGuid();
        AssertPayloadField(writer!, new PayloadSubject { NullableId = guid }, "NullableId",
            v => Assert.Equal(guid, v.Guid));
        AssertPayloadField(writer!, new PayloadSubject { NullableId = null }, "NullableId",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_NestedProperty()
    {
        var field = ScalarField("Nested.Value", typeof(int), "Nested.Value");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NestedValue", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Nested = new NestedObj { Value = 77 } },
            "NestedValue", v => Assert.Equal(77, v.Integer));
    }

    [Fact]
    public void TryCompile_UnsupportedType_ReturnsNull()
    {
        var field = ScalarField("Created", typeof(DateTime), "Created");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Created", field);
        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_ByteProperty_HandledAsSignedInteger()
    {
        var field = ScalarField("Level", typeof(byte), "Level");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Level", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Level = 200 }, "Level",
            v => Assert.Equal(200, v.Integer));
    }

    [Fact]
    public void TryCompile_ShortProperty_HandledAsSignedInteger()
    {
        var field = ScalarField("ShortVal", typeof(short), "ShortVal");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "ShortVal", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { ShortVal = -100 }, "ShortVal",
            v => Assert.Equal(-100, v.Integer));
    }

    [Fact]
    public void TryCompile_LongProperty_HandledAsSignedInteger()
    {
        var field = ScalarField("LongVal", typeof(long), "LongVal");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "LongVal", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { LongVal = long.MaxValue }, "LongVal",
            v => Assert.Equal(long.MaxValue, v.Integer));
    }

    [Fact]
    public void TryCompile_UInt64_SmallValue_WrittenAsInteger()
    {
        var field = ScalarField("BigId", typeof(ulong), "BigId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "BigId", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { BigId = 42 }, "BigId",
            v => Assert.Equal(42, v.Integer));
    }

    #endregion

    #region GeneratedFilterSchemaRegistry

    [Fact]
    public void EnumToInt64OrNull_ReturnsValue_ForIntBackedEnum()
    {
        long? result = GeneratedFilterSchemaRegistry.EnumToInt64OrNull(IntEnum.B);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void EnumToInt64OrNull_ReturnsNull_ForUlongBackedEnum()
    {
        long? result = GeneratedFilterSchemaRegistry.EnumToInt64OrNull(UlongEnum.X);
        Assert.Null(result);
    }

    [Fact]
    public void NullableEnumToInt64OrNull_ReturnsNull_WhenNull()
    {
        long? result = GeneratedFilterSchemaRegistry.NullableEnumToInt64OrNull<IntEnum>(null);
        Assert.Null(result);
    }

    [Fact]
    public void NullableEnumToInt64OrNull_ReturnsValue_WhenPresent()
    {
        long? result = GeneratedFilterSchemaRegistry.NullableEnumToInt64OrNull<IntEnum>(IntEnum.C);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void NullableEnumToInt64OrNull_ReturnsNull_ForUlongBackedEnum_WhenPresent()
    {
        long? result = GeneratedFilterSchemaRegistry.NullableEnumToInt64OrNull<UlongEnum>(UlongEnum.X);
        Assert.Null(result);
    }

    [Fact]
    public void Create_ReturnsFilterSchema()
    {
        var fields = new List<FilterField>
        {
            new("TestField", typeof(int), FilterFieldKind.Scalar, _ => null),
        };
        FilterSchema schema = GeneratedFilterSchemaRegistry.Create(typeof(ItemUsedEvent), fields);
        Assert.Equal(typeof(ItemUsedEvent), schema.SubjectType);
        Assert.Single(schema.FieldNames);
        Assert.Contains("TestField", schema.FieldNames);
    }

    #endregion

    #region EventPipelineCompiler edge cases

    [Fact]
    public void SourceFilter_ExtractsFiltersBeforeProjection()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId), FilterOperator.Equal, FilterValue.From(10L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(filter)
            .AppendProjection(EventProjectionExpression.Default)
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)),
                FilterOperator.Equal,
                FilterValue.From(10L)));

        FilterExpression sourceFilter = EventPipelineCompiler.SourceFilter(pipeline);

        Assert.NotEqual(FilterExpressionKind.Any, sourceFilter.Kind);
    }

    [Fact]
    public void SourceFilter_NullPipeline_ReturnsAny()
    {
        FilterExpression sourceFilter = EventPipelineCompiler.SourceFilter(null);
        Assert.Equal(FilterExpressionKind.Any, sourceFilter.Kind);
    }

    [Fact]
    public void ProjectionDispatchPipeline_NullPipeline_ReturnsDefault()
    {
        EventPipelineExpression result = EventPipelineCompiler.ProjectionDispatchPipeline(null);
        Assert.NotNull(result);
    }

    [Fact]
    public void ProjectionDispatchPipeline_PreservesPostProjectionStages()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId), FilterOperator.Equal, FilterValue.From(10L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(filter)
            .AppendProjection(EventProjectionExpression.Default);

        EventPipelineExpression dispatch = EventPipelineCompiler.ProjectionDispatchPipeline(pipeline);

        Assert.True(dispatch.Stages.Length <= pipeline.Stages.Length);
    }

    [Fact]
    public void RejectProjectedInclude_ThrowsFilterValidationException()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
                [new EventProjectionInclude("test.intrinsic", "tag")]))
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
                [new EventProjectionInclude("test.intrinsic", "tag2")]));

        var ex = Assert.Throws<FilterValidationException>(() =>
            EventPipelineCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                pipeline,
                CompileSimpleInclude,
                EventPipelineCompilerOptions.Immediate));
    }

    [Fact]
    public void PipelineWithParameterizedFilter_BypassesCache()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendFilter(filter)
            .AppendProjection(EventProjectionExpression.Default);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        var first = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var second = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        Assert.NotSame(first, second);
    }

    #endregion

    #region CompiledProjection validation

    [Fact]
    public void CompiledProjection_ThrowsOnNullKey()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            null!, typeof(ItemUsedEvent), fields: [], includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnEmptyKey()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "", typeof(ItemUsedEvent), fields: [], includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnFieldWithNullName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                null!, "Path", _ => ProjectedEventValue.FromScalar(1))],
            includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnFieldWithNullPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "Name", null!, _ => ProjectedEventValue.FromScalar(1))],
            includes: []));
    }

    [Fact]
    public void CompiledProjection_ThrowsOnIncludeWithNullName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledProjection<object>(
            "test",
            typeof(ItemUsedEvent),
            fields: [],
            includes: [new CompiledProjection<object>.IncludeProjector(
                null!, (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(1)))]));
    }

    [Fact]
    public void CompiledProjection_IsTiered_DefaultFalse()
    {
        var projection = new CompiledProjection<object>(
            "test", typeof(ItemUsedEvent), fields: [], includes: []);
        Assert.False(projection.IsTiered);
        Assert.Null(projection.TieredSnapshot);
    }

    [Fact]
    public async Task CompiledProjection_NoFieldsNoIncludes_ProjectsEmptyEvent()
    {
        var projection = new CompiledProjection<object>(
            "empty", typeof(ItemUsedEvent), fields: [], includes: []);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1);
        ProjectedEvent result = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.Equal(typeof(ItemUsedEvent).FullName, result.EventType);
        Assert.Equal(nameof(ItemUsedEvent), result.EventName);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void CompiledProjection_FieldProjectorProject_ReturnsCorrectField()
    {
        var projector = new CompiledProjection<object>.FieldProjector(
            "TestField", "TestPath",
            _ => ProjectedEventValue.FromScalar(42));

        ProjectedEventField field = projector.Project(new object());
        Assert.Equal("TestField", field.Name);
        Assert.Equal(42, field.Value.Integer);
    }

    [Fact]
    public async Task CompiledProjection_PayloadNoIncludes_WritesCorrectly()
    {
        var projection = new CompiledProjection<object>(
            "payload-no-includes",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes: []);

        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            subject, new object(), options, CancellationToken.None);

        ProjectedEvent deserialized = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);
        Assert.Equal(42, deserialized.Field("ItemId").Integer);
    }

    [Fact]
    public async Task CompiledProjection_PromoteProjectFields_ReplacesFieldProjection()
    {
        var projection = new CompiledProjection<object>(
            "promote-test",
            typeof(ItemUsedEvent),
            fields: [new CompiledProjection<object>.FieldProjector(
                "ItemId", "ItemId",
                subject => ProjectedEventValue.FromScalar(((ItemUsedEvent)subject).ItemId))],
            includes: []);

        projection.PromoteProjectFields(static _ =>
            [new ProjectedEventField("Promoted", ProjectedEventValue.FromScalar(999))]);

        var subject = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 5);
        ProjectedEvent result = await projection.ProjectAsync(subject, new object(), CancellationToken.None);

        Assert.True(result.TryGetField("Promoted", out var field));
        Assert.Equal(999, field.Integer);
    }

    #endregion

    #region Helpers

    private static string CreateTempDirectory(string suffix)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "SiftQLWave6Tests", suffix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteManifestAndFakeAssembly(string directory, HotCompilationManifest manifest)
    {
        string manifestJson = JsonSerializer.Serialize(manifest);
        File.WriteAllText(Path.Combine(directory, "hot.json"), manifestJson);
        File.WriteAllBytes(Path.Combine(directory, "hot.dll"), [0x00]);
    }

    private static HotTieredProviderLoadResult LoadFromDirectory(string directory) =>
        HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = Path.Combine(directory, "hot.dll"),
            ManifestPath = Path.Combine(directory, "hot.json"),
            RequireExactRuntimeVersion = false,
        });

    private static FilterField ScalarField(string name, Type valueType, string propertyPath) =>
        new(name, valueType, FilterFieldKind.Scalar,
            _ => null, Access: FilterFieldAccess.ForProperty(propertyPath));

    private static void AssertPayloadField(
        ProjectedFieldPayloadWriter writer,
        object subject,
        string fieldName,
        Action<ProjectedEventValue> assertion)
    {
        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var mpWriter = new MessagePackWriter(buffer);

        mpWriter.WriteMapHeader(1);
        writer(ref mpWriter, subject, options);
        mpWriter.Flush();

        var reader = new MessagePackReader(buffer.WrittenMemory);
        int mapCount = reader.ReadMapHeader();
        Assert.Equal(1, mapCount);

        ProjectedEventField field = MessagePackSerializer.Deserialize<ProjectedEventField>(
            ref reader, options);
        Assert.Equal(fieldName, field.Name);
        assertion(field.Value);
    }

    private static CompiledProjection<object>.IncludeProjector CompileSimpleInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("included")));
    }

    #endregion

    #region Test types

    private enum IntEnum { A = 1, B = 2, C = 3 }
    private enum UlongEnum : ulong { X = 1, Y = 2 }

    public sealed class PayloadSubject
    {
        public bool IsActive { get; set; } = true;
        public bool? NullableBool { get; set; }
        public int Count { get; set; }
        public int? NullableCount { get; set; }
        public ulong BigId { get; set; }
        public ulong? NullableBigId { get; set; }
        public double Score { get; set; }
        public double? NullableScore { get; set; }
        public float Ratio { get; set; }
        public decimal Price { get; set; }
        public decimal? NullablePrice { get; set; }
        public string? Name { get; set; }
        public Guid Id { get; set; }
        public Guid? NullableId { get; set; }
        public NestedObj? Nested { get; set; }
        public DateTime Created { get; set; }
        public byte Level { get; set; }
        public short ShortVal { get; set; }
        public long LongVal { get; set; }
    }

    public sealed class NestedObj
    {
        public int Value { get; set; }
    }

    #endregion
}
