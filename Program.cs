using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("WorkFlowDb")
    ?? throw new InvalidOperationException("ConnectionStrings:WorkFlowDb yapılandırılmalıdır.");

builder.Services.AddDbContext<WorkFlowDbContext>(options => options.UseNpgsql(connectionString));

// Proje adı yalnızca arayüzde gösterilir; görevler veritabanında saklanır.
builder.Services.AddSingleton(new Project("Staj Görevlerim"));
builder.Services.AddScoped<ITaskService, TaskService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Tasks/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Tasks}/{action=Index}/{id?}");

app.Run();
