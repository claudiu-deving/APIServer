﻿using ccsflowserver.Model;
using ccsflowserver.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

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
		_configuration = configuration;
		_authService = authService;
		_userService = userService;
	}


	[HttpGet("role")]
	[Authorize]
	public async Task<IActionResult> GetRole()
	{
		var user = HttpContext.User;
		var userId = user.Claims.FirstOrDefault(x => x.Type.Equals("id"))?.Value;
		if (userId is not null)
		{
			var idToGuid = Guid.Parse(userId);
			var existing = await _userService.Get(idToGuid);

			if (existing.Success && existing.Data is not null)
			{
				return Ok(new { existing.Data.Role.Name, existing.Data.Username, existing.Data.Email });
			}
			else
			{
				return NotFound(existing.Message);
			}
		}
		else
		{
			return NotFound("No user with the given id was found");
		}

	}




	[HttpGet("admin/{username}")]
	public async Task<IActionResult> IsAdmin(string username)
	{
		var data = await _userService.Get();
		if (data.Success && data.Data is not null)
		{
			var user = data.Data.FirstOrDefault(x => x.Username.Equals(username));
			if (user is null)
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
		if (data.Success && data.Data is not null)
		{
			var user = data.Data.FirstOrDefault(x => x.Username.Equals(username));
			if (user is null)
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
		if (UserExceedesRetriesLimit(user))
		{
			return Forbid();
		}
		var dbUser = await _authService.Verify(user.Username, user.Password);
		if (dbUser is null)
		{
			return Unauthorized();
		}

		var jtwKey = Environment.GetEnvironmentVariable("JWT_KEY");
		if (string.IsNullOrEmpty(jtwKey))
		{
			Console.WriteLine("No environment variable found at: JWT_KEY");
			return StatusCode(500);
		}
		var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jtwKey));
		var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, user.Username),
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
			new(ClaimTypes.Role, dbUser.Role.Name),
			new("id", dbUser.Id.ToString())
		};

		var token = new JwtSecurityToken(
			issuer: _configuration["Jwt:Issuer"],
			audience: _configuration["Jwt:Audience"],
			claims: claims,
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

	private static Dictionary<string, List<DateTime>> _lastTry = new();
	private bool UserExceedesRetriesLimit(UserPayloadVerification user)
	{
		var valueRetrieved = _lastTry.TryGetValue(user.Username, out var listOfDates);

		if (valueRetrieved)
		{
			listOfDates!.Add(DateTime.Now);

			listOfDates.Sort();

			if (listOfDates.Count == 5)
			{
				if ((listOfDates[0] - listOfDates[listOfDates.Count - 1]).Duration() < TimeSpan.FromMinutes(5).Duration())
				{
					Console.WriteLine($"User {user.Username} tried to login more than 5 times");
					listOfDates.RemoveAt(0);
					return true;
				}
				listOfDates.RemoveAt(0);
			}

			_lastTry[user.Username] = listOfDates;
			return false;
		}
		else
		{
			listOfDates = [];
			_lastTry[user.Username] = listOfDates;
			return false;
		}
	}

	[HttpPost("register")]
	public async Task<IActionResult> RegisterUser(UserPayloadRegistration user)
	{
		if (string.IsNullOrEmpty(user.Username) || string.IsNullOrEmpty(user.Password))
			return BadRequest("Username or password is missing");

		if (await _authService.UserExists(user.Username))
		{
			return BadRequest("Username already exists");
		}

		if (await _authService.EmailExists(user.Email))
		{
			return BadRequest("Email already exists");
		}

		var userResponse = await _authService.RegisterUser(user);

		return Ok(userResponse);
	}

	[HttpPost("google/callback")]
	public async Task<IActionResult> LoginWithGoogle([FromBody] string credentials)
	{
		var googleId = Environment.GetEnvironmentVariable("GOOGLE_ID");
		if (string.IsNullOrEmpty(googleId)) return StatusCode(500, "The server is not set up correctly");
		var settings = new GoogleJsonWebSignature.ValidationSettings()
		{
			Audience = [googleId]
		};

		var payload = await GoogleJsonWebSignature.ValidateAsync(credentials, settings);

		var users = await _userService.Get();
		if (users.Success)
		{
			var user = users.Data!.FirstOrDefault(x => x.Email == payload.Email);
			if (user != null)
			{
				return Ok(user);
			}
			else
			{
				return BadRequest();
			}
		}
		else
		{
			return BadRequest();
		}
	}
}
