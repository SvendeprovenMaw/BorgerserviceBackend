using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Backend.api.Middleware
{
    public class CustomExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public CustomExceptionHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context); // Continue to the Controller
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            Console.WriteLine(exception);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 500;
            var endpoint = context.GetEndpoint();
            var controllerName = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerName;

            var response = new { message = "An error occurred!", detail = exception.Message };
            return context.Response.WriteAsJsonAsync(response);
        }
    }
}