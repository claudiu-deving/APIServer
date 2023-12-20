﻿using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using ccsflowserver.Model;
using ccsflowserver.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ccsflowserver.AA;
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IAuthservice _authService;
    private readonly IModelService<User> _userService;

    public AuthController(IConfiguration configuration, IAuthservice authService, IModelService<User> userService)
    {
        _configuration=configuration;
        _authService=authService;
        _userService=userService;
    }


    [HttpGet("admin/{username}")]
    public async Task<IActionResult> IsAdmin(string username)
    {
        var data = await _userService.Get();
        if(data.Success&&data.Data is not null)
        {
            var user = data.Data.FirstOrDefault(x => x.Username.Equals(username));
            if(user is null)
            {
                return NotFound("User not found");
            }
            return Ok(user.Role.IsAdmin);
        }
        else
        {
            return NotFound(data.Message);
        }
    }



    [HttpGet("{username}")]
    public async Task<IActionResult> Get(string username)
    {
        var data = await _userService.Get();
        if(data.Success&&data.Data is not null)
        {
            var user = data.Data.FirstOrDefault(x => x.Username.Equals(username));
            if(user is null)
            {
                return NotFound("User not found");
            }
            return Ok(user.Id);
        }
        else
        {
            return NotFound(data.Message);
        }
    }


    [HttpPost("token")]
    public async Task<IActionResult> GenerateToken(UserPayloadVerification user)
    {
        if(UserExceedesRetriesLimit(user))
        {
            return Forbid();
        }
        var dbUser = await _authService.Verify(user.Username, user.Password);
        if(dbUser is null)
        {
            return Unauthorized();
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            expires: DateTime.Now.AddMinutes(Convert.ToDouble(_configuration["Jwt:DurationInMinutes"])),
            signingCredentials: credentials
        );

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiration = token.ValidTo,
            userId = dbUser.Id
        });
    }

    private static Dictionary<string, List<DateTime>> _lastTry = new Dictionary<string, List<DateTime>>();
    private bool UserExceedesRetriesLimit(UserPayloadVerification user)
    {
        var valueRetrieved = _lastTry.TryGetValue(user.Username, out var listOfDates);

        if(valueRetrieved)
        {
            listOfDates!.Add(DateTime.Now);

            listOfDates.Sort();

            if(listOfDates.Count==5)
            {
                if((listOfDates[0]-listOfDates[listOfDates.Count-1]).Duration()<TimeSpan.FromMinutes(5).Duration())
                {
                    Console.WriteLine($"User {user.Username} tried to login more than 5 times");
                    listOfDates.RemoveAt(0);
                    return true;
                }
                listOfDates.RemoveAt(0);
            }

            _lastTry[user.Username]=listOfDates;
            return false;
        }
        else
        {
            listOfDates=new List<DateTime>();
            _lastTry[user.Username]=listOfDates;
            return false;
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser(UserPayloadRegistration user)
    {
        if(string.IsNullOrEmpty(user.Username)||string.IsNullOrEmpty(user.Password))
            return BadRequest("Username or password is missing");

        if(await _authService.UserExists(user.Username))
        {
            return BadRequest("Username already exists");
        }

        if(await _authService.EmailExists(user.Email))
        {
            return BadRequest("Email already exists");
        }

        var userResponse = await _authService.RegisterUser(user);

        return Ok(userResponse);
    }
}
