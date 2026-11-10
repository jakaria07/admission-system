using AdmissionSystem.Application.Classes;
using AdmissionSystem.Application.Holds;
using Microsoft.Extensions.DependencyInjection;

namespace AdmissionSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ClassAppService>();
        services.AddScoped<HoldAppService>();
        return services;
    }
}

