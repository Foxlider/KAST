var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.KAST>("kast")
    .WithOtlpExporter();

await builder.Build().RunAsync();
