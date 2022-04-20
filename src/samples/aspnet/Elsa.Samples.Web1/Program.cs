using System;
using System.Text.Json;
using Elsa.Activities;
using Elsa.Api.Extensions;
using Elsa.Contracts;
using Elsa.Extensions;
using Elsa.Jobs.Extensions;
using Elsa.Management.Extensions;
using Elsa.Modules.Activities.Configurators;
using Elsa.Modules.Activities.Console;
using Elsa.Modules.Activities.Workflows;
using Elsa.Modules.AzureServiceBus.Activities;
using Elsa.Modules.AzureServiceBus.Extensions;
using Elsa.Modules.Hangfire.Services;
using Elsa.Modules.Http;
using Elsa.Modules.Http.Extensions;
using Elsa.Modules.JavaScript.Activities;
using Elsa.Modules.Quartz.Services;
using Elsa.Modules.Scheduling.Activities;
using Elsa.Modules.Scheduling.Extensions;
using Elsa.Modules.WorkflowContexts.Extensions;
using Elsa.Persistence.EntityFrameworkCore.Extensions;
using Elsa.Persistence.EntityFrameworkCore.Sqlite;
using Elsa.Pipelines.WorkflowExecution.Components;
using Elsa.Runtime.Extensions;
using Elsa.Runtime.ProtoActor.Extensions;
using Elsa.Samples.Web1.Activities;
using Elsa.Samples.Web1.Models;
using Elsa.Samples.Web1.Serialization;
using Elsa.Samples.Web1.Workflows;
using Elsa.Scripting.JavaScript.Extensions;
using Elsa.Scripting.Liquid.Extensions;
using Elsa.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;
var configuration = builder.Configuration;

// Run the SqlServer container from docker-compose.yml to start a SQL Server container.
var sqlServerConnectionString = configuration.GetConnectionString("SqlServer");

// Add services.
services
    .AddElsa()
    .AddEntityFrameworkCorePersistence((_, ef) => ef.UseSqlite())
    .AddProtoActorWorkflowHost()
    .IndexWorkflowTriggers()
    .AddElsaManagement()
    .AddJobServices(new QuartzJobSchedulerProvider(), new HangfireJobQueueProvider(sqlServerConnectionString))
    .AddSchedulingServices()
    .AddHttpActivityServices()
    .AddAzureServiceBusServices(options => configuration.GetSection("AzureServiceBus").Bind(options))
    .ConfigureWorkflowRuntime(options =>
    {
        // Register workflows.
        options.Workflows.Add<HelloWorldWorkflow>();
        //options.Workflows.Add<HeartbeatWorkflow>();
        options.Workflows.Add<HttpWorkflow>();
        options.Workflows.Add<ForkedHttpWorkflow>();
        options.Workflows.Add<CompositeActivitiesWorkflow>();
        options.Workflows.Add<SendMessageWorkflow>();
        options.Workflows.Add<ReceiveMessageWorkflow>();
        options.Workflows.Add<RunJavaScriptWorkflow>();
        options.Workflows.Add<WorkflowContextsWorkflow>();
        options.Workflows.Add<SubmitJobWorkflow>();
        options.Workflows.Add<DelayWorkflow>();
        options.Workflows.Add<OrderProcessingWorkflow>();
    });

// Testing only: allow client app to connect from anywhere.
services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

// Register activities available from the designer.
services
    .AddActivity<WriteLine>()
    .AddActivity<WriteLines>()
    .AddActivity<ReadLine>()
    .AddActivity<If>()
    .AddActivity<HttpEndpoint>()
    .AddActivity<Flowchart>()
    .AddActivity<Delay>()
    .AddActivity<Timer>()
    .AddActivity<ForEach>()
    .AddActivity<Switch>()
    .AddActivity<SendMessage>()
    .AddActivity<RunJavaScript>()
    ;

// Register scripting languages.
services
    .AddJavaScriptExpressions()
    .AddLiquidExpressions();

// Register serialization configurator for configuring what types to allow to be serialized.
services.AddSingleton<ISerializationOptionsConfigurator, CustomSerializationOptionConfigurator>();
services.AddSingleton<ISerializationOptionsConfigurator, SerializationOptionsConfigurator>();

// Configure middleware pipeline.
var app = builder.Build();
var serviceProvider = app.Services;

// Add type aliases for prettier JSON serialization.
var wellKnownTypeRegistry = serviceProvider.GetRequiredService<IWellKnownTypeRegistry>();
wellKnownTypeRegistry.RegisterType<int>("int");
wellKnownTypeRegistry.RegisterType<float>("float");
wellKnownTypeRegistry.RegisterType<bool>("boolean");
wellKnownTypeRegistry.RegisterType<string>("string");

var order = new Order("order-1", 1, "customer-1", new[] { new OrderItem("product-i1", 2) });
var serializationOptions = serviceProvider.GetRequiredService<WorkflowSerializerOptionsProvider>().CreatePersistenceOptions();
var json = JsonSerializer.Serialize(order, serializationOptions);
Console.WriteLine(json);

// Configure workflow engine execution pipeline.
serviceProvider.ConfigureDefaultWorkflowExecutionPipeline(pipeline =>
    pipeline
        .UseWorkflowExecutionEvents()
        .UseWorkflowExecutionLogPersistence()
        .UsePersistence()
        .UseWorkflowContexts()
        .UseActivityScheduler()
);

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

// CORS.
app.UseCors();

// Root.
app.MapGet("/", () => "Hello World!");

// Map Elsa API endpoints.
app.MapElsaApiEndpoints();

// Register Elsa middleware.
app.UseJsonSerializationErrorHandler();
app.UseHttpActivities();

// Run.
app.Run();