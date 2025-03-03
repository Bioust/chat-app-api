using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ChatApp.API.Controllers
{
    // Controllers/TestController.cs
[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Test()
    {
        Response.Headers.Append("Access-Control-Allow-Origin", "*");
        Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Accept");
        
        return Ok(new { 
            message = "API is working!",
            timestamp = DateTime.UtcNow,
            clientIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
    }
}
}