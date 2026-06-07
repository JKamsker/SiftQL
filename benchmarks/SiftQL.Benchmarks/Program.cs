namespace SiftQL.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkOptions options = BenchmarkOptions.Parse(args);
        IBenchmarkCase[] cases =
        [
            new SimpleFilterCase(),
            new ScalarFilterCase(),
            new TieredInterpretedFilterCase(),
            new TieredPromotedFilterCase(),
            new ComplexFilterCase(),
            new LargeInFilterCase(),
            new ClauseOrderingFilterCase(),
            new OpcodeEvaluatorCase(),
            new FilterRegistrationCase(),
            new TieredFilterRegistrationCase(),
            new PluginFilterRegistrationCase(),
            new DispatchScanCase(),
            new DispatchIndexCase(),
            new ServerProjectedDispatchPipelineCase(),
            new ClientProjectedDispatchPipelineCase(),
            new TwoFieldProjectionCase(),
            new TieredTwoFieldProjectionCase(),
            new TieredPromotedTwoFieldProjectionCase(),
            new DefaultProjectionCase(),
            new IncludeProjectionCase(),
            new ProjectionGroupingCase(),
            new FilterProjectionPipelineCase(),
            new ProjectedPayloadSerializationCase(),
        ];

        BenchmarkRunner.Run(cases, options);
    }
}
