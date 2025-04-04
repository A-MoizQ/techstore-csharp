using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;
using System.Text.Json;


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

// Set up users database
string usersDbPath = "databases/users.db";
Directory.CreateDirectory("databases");

using (var connection = new SqliteConnection($"Data Source={usersDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE IF NOT EXISTS users (uname TEXT PRIMARY KEY, password TEXT NOT NULL);";
    command.ExecuteNonQuery();
}

// Set up products database
string productsDbPath = "databases/products.db";

using (var connection = new SqliteConnection($"Data Source={productsDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL, 
            description TEXT,
            category TEXT NOT NULL,
            image BLOB,
            rating INTEGER NOT NULL,
            price REAL,
            isTop INTEGER
        );
    ";
    command.ExecuteNonQuery();
}

// Set up cart database table 

string cartDbPath = "databases/cart.db";
using (var connection = new SqliteConnection($"Data Source={cartDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS cart (
            cart_id INTEGER PRIMARY KEY AUTOINCREMENT,
            uname TEXT NOT NULL,
            product_id INTEGER NOT NULL,
            quantity INTEGER NOT NULL
        );
    ";
    command.ExecuteNonQuery();
}


app.MapPost("/api/auth/signup", async (HttpContext context) =>
{
    var authRequest = await context.Request.ReadFromJsonAsync<AuthRequest>();
    if (authRequest is null)
    {
        return Results.BadRequest("Invalid payload.");
    }
    string uname = authRequest.Username.ToLower();
    string password = authRequest.Password;

    if (!Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[_#@$]).{8,}$"))
        return Results.BadRequest("Invalid password format");

    using (var connection = new SqliteConnection($"Data Source={usersDbPath}"))
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

// Login Endpoint
app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var authRequest = await context.Request.ReadFromJsonAsync<AuthRequest>();
    if (authRequest is null)
    {
        return Results.BadRequest("Invalid payload.");
    }
    string uname = authRequest.Username.ToLower();
    string password = authRequest.Password;

    using (var connection = new SqliteConnection($"Data Source={usersDbPath}"))
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

app.MapPost("/admin", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();

    string name = form["name"].ToString();
    string description = form["description"].ToString();
    string category = form["category"].ToString();
    int.TryParse(form["rating"].ToString(), out int rating);
    double.TryParse(form["price"].ToString(), out double price);
    int.TryParse(form["isTop"].ToString(), out int isTop); // 0 = false, 1 = true

    // Handling image file
    byte[]? imageBytes = null;
    if (form.Files.Count > 0)
    {
        var imageFile = form.Files["image"];
        if (imageFile != null)
        {
            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }
    }

    using (var connection = new SqliteConnection($"Data Source={productsDbPath}"))
    {
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO products (name, description, category, image, rating, price, isTop) 
            VALUES (@name, @description, @category, @image, @rating, @price, @isTop)";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@description", description);
        command.Parameters.AddWithValue("@category", category);
        command.Parameters.AddWithValue("@image", imageBytes == null ? DBNull.Value : (object)imageBytes);
        command.Parameters.AddWithValue("@rating", rating);
        command.Parameters.AddWithValue("@price", price);
        command.Parameters.AddWithValue("@isTop", isTop);

        command.ExecuteNonQuery();
    }

    return Results.Ok("Product added successfully.");
});

app.MapGet("/product/{id:int}", async (int id) =>
{
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT id, name, description, image, rating, price, isTop 
        FROM products 
        WHERE id = @id";
    command.Parameters.AddWithValue("@id", id);
    
    using var reader = command.ExecuteReader();
    if (reader.Read())
    {
        var product = new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
            rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            price = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
            isTop = reader.IsDBNull(6) ? false : reader.GetInt32(6) == 1
        };
        return Results.Json(product);
    }
    return Results.NotFound("Product not found.");
});

app.MapGet("/topselling",async () =>
{
    string productsDbPath = "databases/products.db";
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT id, name, description, image, rating, price, isTop 
        FROM products 
        WHERE isTop = 1
        ORDER BY name ASC";
    
    using var reader = command.ExecuteReader();
    var products = new List<object>();
    while (reader.Read())
    {
        var product = new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
            rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            price = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
            isTop = reader.IsDBNull(6) ? false : reader.GetInt32(6) == 1
        };
        products.Add(product);
    }
    return Results.Json(products);
});


app.MapGet("/homepage", () =>
{
    string productsDbPath = "databases/products.db";
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();

    // Get top selling products (isTop = 1)
    var productCommand = connection.CreateCommand();
    productCommand.CommandText = @"
        SELECT id, name, description, image, rating, price, isTop 
        FROM products 
        WHERE isTop = 1
        ORDER BY name ASC";
    var products = new List<object>();
    using (var reader = productCommand.ExecuteReader())
    {
        while (reader.Read())
        {
            var product = new
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2),
                image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
                rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                price = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
                isTop = reader.IsDBNull(6) ? false : reader.GetInt32(6) == 1
            };
            products.Add(product);
        }
    }

    // Get distinct categories with the image of the first product in each category
    var categoryDisplayList = new List<object>();
    var distinctCmd = connection.CreateCommand();
    distinctCmd.CommandText = "SELECT DISTINCT category FROM products ORDER BY category ASC";
    using (var reader = distinctCmd.ExecuteReader())
    {
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                var categoryName = reader.GetString(0);
                var imageCmd = connection.CreateCommand();
                imageCmd.CommandText = "SELECT image FROM products WHERE category = @cat AND image IS NOT NULL ORDER BY name ASC LIMIT 1";
                imageCmd.Parameters.AddWithValue("@cat", categoryName);
                object imageObj = imageCmd.ExecuteScalar();
                string? imageBase64 = (imageObj != null && imageObj != DBNull.Value)
                    ? Convert.ToBase64String((byte[])imageObj)
                    : null;
                categoryDisplayList.Add(new { category = categoryName, image = imageBase64 });
            }
        }
    }

    return Results.Json(new { products, categories = categoryDisplayList });
});

app.MapGet("/categories", () =>
{
    string productsDbPath = "databases/products.db";
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();

    var distinctCmd = connection.CreateCommand();
    distinctCmd.CommandText = "SELECT DISTINCT category FROM products ORDER BY category ASC";
    var categories = new List<object>();
    
    using (var reader = distinctCmd.ExecuteReader())
    {
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                string cat = reader.GetString(0);
                // Get the image of the first product in this category (if exists)
                var imageCmd = connection.CreateCommand();
                imageCmd.CommandText = "SELECT image FROM products WHERE category = @cat AND image IS NOT NULL ORDER BY name ASC LIMIT 1";
                imageCmd.Parameters.AddWithValue("@cat", cat);
                object imageObj = imageCmd.ExecuteScalar();
                string? imageBase64 = (imageObj != null && imageObj != DBNull.Value)
                    ? Convert.ToBase64String((byte[])imageObj)
                    : null;
                categories.Add(new { category = cat, image = imageBase64 });
            }
        }
    }
    return Results.Json(categories);
});


// Add to cart endpoint
app.MapPost("/cart", async (HttpContext context) =>
{
    var jsonDoc = await JsonDocument.ParseAsync(context.Request.Body);
    var root = jsonDoc.RootElement;

    if (!root.TryGetProperty("uname", out var unameProp) ||
        !root.TryGetProperty("productId", out var productIdProp) ||
        !root.TryGetProperty("quantity", out var quantityProp))
    {
        return Results.BadRequest("Missing one or more required fields.");
    }

    string uname = unameProp.GetString()?.ToLower() ?? "";
    int productId = productIdProp.GetInt32();
    int quantity = quantityProp.GetInt32();

    // Ensure the user exists before adding to cart
    using var userConnection = new SqliteConnection($"Data Source={usersDbPath}");
    userConnection.Open();
    var userCheckCmd = userConnection.CreateCommand();
    userCheckCmd.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname";
    userCheckCmd.Parameters.AddWithValue("@uname", uname);
    long userExists = (long)userCheckCmd.ExecuteScalar();

    if (userExists == 0)
    {
        return Results.BadRequest("User does not exist.");
    }

    // Add or update item in cart
    using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    connection.Open();

    // Check if the item is already in the cart
    var checkCmd = connection.CreateCommand();
    checkCmd.CommandText = @"
        SELECT quantity FROM cart
        WHERE uname = @uname AND product_id = @product_id";
    checkCmd.Parameters.AddWithValue("@uname", uname);
    checkCmd.Parameters.AddWithValue("@product_id", productId);

    var existingQuantityObj = checkCmd.ExecuteScalar();

    if (existingQuantityObj != null)
    {
        // Item exists, update the quantity
        int existingQuantity = Convert.ToInt32(existingQuantityObj);
        int newQuantity = existingQuantity + quantity;

        var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE cart
            SET quantity = @newQuantity
            WHERE uname = @uname AND product_id = @product_id";
        updateCmd.Parameters.AddWithValue("@newQuantity", newQuantity);
        updateCmd.Parameters.AddWithValue("@uname", uname);
        updateCmd.Parameters.AddWithValue("@product_id", productId);

        try
        {
            updateCmd.ExecuteNonQuery();
            return Results.Ok("Cart item quantity updated.");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to update cart item: {ex.Message}");
        }
    }
    else
    {
        // Item does not exist, insert new row
        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO cart (uname, product_id, quantity)
            VALUES (@uname, @product_id, @quantity)";
        insertCmd.Parameters.AddWithValue("@uname", uname);
        insertCmd.Parameters.AddWithValue("@product_id", productId);
        insertCmd.Parameters.AddWithValue("@quantity", quantity);

        try
        {
            insertCmd.ExecuteNonQuery();
            return Results.Ok("Item added to cart.");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to add item to cart: {ex.Message}");
        }
    }
});


// Get all cart items for a user by their username
app.MapGet("/cart/{uname}", (string uname) =>
{
    using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    connection.Open();

    // Attach the products database so we can join product details
    var attachCmd = connection.CreateCommand();
    attachCmd.CommandText = $"ATTACH DATABASE '{productsDbPath}' AS prod;";
    attachCmd.ExecuteNonQuery();

    // Join cart items with corresponding product details
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT 
            c.cart_id,
            c.product_id,
            p.name AS product_name,
            p.image AS product_image,
            p.price AS product_price,
            c.quantity
        FROM cart c
        JOIN prod.products p ON c.product_id = p.id
        WHERE LOWER(c.uname) = LOWER(@uname);";
    command.Parameters.AddWithValue("@uname", uname);

    var items = new List<object>();
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        // Read data and prepare it for frontend consumption
        items.Add(new
        {
            cart_id = reader.GetInt32(0),
            product_id = reader.GetInt32(1),
            product_name = reader.GetString(2),
            product_image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
            product_price = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4),
            quantity = reader.GetInt32(5)
        });
    }

    return Results.Json(items);
});


// clear the cart (by option or at checkout)
app.MapDelete("/cart/clear/{uname}", (string uname) =>
{
    using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM cart WHERE uname = @uname";
    command.Parameters.AddWithValue("@uname", uname.ToLower());
    int deleted = command.ExecuteNonQuery();
    return Results.Ok($"{deleted} item(s) removed from cart.");
});

app.Run();

public record AuthRequest(string Username, string Password);
