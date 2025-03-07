using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Add services for Blazor
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

string dbPath = "databases/users.db";
Directory.CreateDirectory("databases");
Directory.CreateDirectory("wwwroot/images");

using (var connection = new SqliteConnection($"Data Source={dbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE IF NOT EXISTS users (uname TEXT PRIMARY KEY, password TEXT NOT NULL);";
    command.ExecuteNonQuery();
}

// API routes for login/signup
app.MapPost("/signup", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string uname = form["uname"].ToString().ToLower();
    string password = form["password"];

    if (!Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[_#@$]).{8,}$"))
        return Results.BadRequest("Invalid password format");

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname";
        checkCmd.Parameters.AddWithValue("@uname", uname);
        if ((long)checkCmd.ExecuteScalar() > 0)
            return Results.BadRequest("Username already exists");

        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO users (uname, password) VALUES (@uname, @password)";
        insertCmd.Parameters.AddWithValue("@uname", uname);
        insertCmd.Parameters.AddWithValue("@password", password);
        insertCmd.ExecuteNonQuery();
    }
    return Results.Ok("Signup successful");
});

app.MapPost("/login", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string uname = form["uname"].ToString().ToLower();
    string password = form["password"];

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname AND password = @password";
        command.Parameters.AddWithValue("@uname", uname);
        command.Parameters.AddWithValue("@password", password);
        if ((long)command.ExecuteScalar() > 0)
            return Results.Ok("Login successful");
    }
    return Results.BadRequest("Invalid credentials");
});

// Enable Blazor routing
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
