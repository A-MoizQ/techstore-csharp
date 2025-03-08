using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5014") // Updated to match the frontend
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


builder.Services.AddControllers();
var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

// Your custom endpoints (or use Controllers)
// Example: Use minimal APIs for login/signup:
string dbPath = "databases/users.db";
Directory.CreateDirectory("databases");

// Create or open SQLite database and table...
using (var connection = new SqliteConnection($"Data Source={dbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE IF NOT EXISTS users (uname TEXT PRIMARY KEY, password TEXT NOT NULL);";
    command.ExecuteNonQuery();
}

dbPath = "databases/products.db";

using (var connection = new SqliteConnection($"Data Source={dbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL, -- equivalent to varchar(128)
            description TEXT,   -- equivalent to varchar(256)
            image BLOB,
            rating INTEGER,     -- int2 can be stored as INTEGER
            price REAL,         -- REAL used for currency values
            isTop INTEGER       -- BOOLEAN simulated as INTEGER (0 = false, 1 = true)
        );
    ";
    command.ExecuteNonQuery();
}


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

app.Run();
