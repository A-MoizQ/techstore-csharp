using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http;
using TechStore.Frontend;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// Set the BaseAddress to your backend API URL
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("https://localhost:5001/") });

await builder.Build().RunAsync();
