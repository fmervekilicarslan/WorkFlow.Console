using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllersWithViews();

// Bellekteki tek proje ve servis bütün HTTP isteklerinde paylaşılır.
builder.Services.AddSingleton(new Project("Staj Görevlerim"));
builder.Services.AddSingleton<ITaskService, TaskService>();

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
