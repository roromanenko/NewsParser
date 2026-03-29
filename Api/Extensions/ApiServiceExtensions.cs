using Api.Validators;
using Core.Options;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

namespace Api.Extensions;

public static class ApiServiceExtensions
{
	public static IServiceCollection AddApi(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddControllers();
		services.AddFluentValidationAutoValidation();
		services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
		services.AddSwagger();
		services.AddJwt(configuration);

		services.AddCors(options =>
		{
			options.AddPolicy("AllowFrontend", policy =>
			{
				policy.WithOrigins("http://localhost:5173")
					  .AllowAnyHeader()
					  .AllowAnyMethod()
					  .AllowCredentials();
			});
		});

		return services;
	}

	private static IServiceCollection AddSwagger(this IServiceCollection services)
	{
		services.AddEndpointsApiExplorer();
		services.AddSwaggerGen(options =>
		{
			options.SwaggerDoc("v1", new OpenApiInfo
			{
				Title = "Media Platform API",
				Version = "v1"
			});

			options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
			{
				Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
				Name = "Authorization",
				In = ParameterLocation.Header,
				Type = SecuritySchemeType.ApiKey,
				Scheme = "Bearer"
			});

			options.AddSecurityRequirement(new OpenApiSecurityRequirement
		{
			{
				new OpenApiSecurityScheme
				{
					Reference = new OpenApiReference
					{
						Type = ReferenceType.SecurityScheme,
						Id = "Bearer"
					}
				},
				Array.Empty<string>()
			}
		});
		});

		return services;
	}


	private static IServiceCollection AddJwt(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
			?? throw new InvalidOperationException("JWT options are not configured");

		services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

		services
			.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(options =>
			{
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidIssuer = jwtOptions.Issuer,
					ValidAudience = jwtOptions.Audience,
					IssuerSigningKey = new SymmetricSecurityKey(
						Encoding.UTF8.GetBytes(jwtOptions.SecretKey))
				};
			});

		services.AddAuthorization();
		return services;
	}
}