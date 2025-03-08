using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TechStore.Frontend;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// Read the backend URL from configuration
var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:5090/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(backendUrl) });

await builder.Build().RunAsync();
