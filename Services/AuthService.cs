﻿using ccsflowserver.Data;
using ccsflowserver.Model;

using Microsoft.EntityFrameworkCore;

namespace ccsflowserver.Services;

public class AuthService : IAuthservice
{
    private readonly AppDbContext _appDbContext;
    private readonly IPasswordManager _passwordManager;

    public AuthService(AppDbContext appDbContext, IPasswordManager passwordManager)
    {
        _appDbContext=appDbContext;
        _passwordManager=passwordManager;
    }

    public async Task<bool> UserExists(string username)
    {
        var dbUserName = await _appDbContext.Users.FirstOrDefaultAsync(u => u.Username==username);
        if(dbUserName is null)
        {
            return false;
        }
        else
        {
            return true;
        }
    }


    public async Task<bool> Verify(string username, string password)
    {
        var dbUserName = await _appDbContext.Users.FirstOrDefaultAsync(u => u.Username==username);

        if(dbUserName is null)
        {
            return false;
        }
        var passwordHash = _passwordManager.HashPassword(password, dbUserName.PassSalt);

        var result = Convert.ToBase64String(dbUserName.PassHash).Equals(Convert.ToBase64String(passwordHash));

        return result;
    }

    public async Task<User> RegisterUser(UserPayloadRegistration user)
    {
        var hashedDetails = _passwordManager.HashNewPassword(user.Password);
        User login = new User()
        {
            Username=user.Username,
            PassHash=hashedDetails.hash,
            PassSalt=hashedDetails.salt,
            Email=user.Email,
            Id=Guid.NewGuid(),
            Role=Role.Default
        };
        await _appDbContext.Users.AddAsync(login);
        await _appDbContext.SaveChangesAsync();
        return login;
    }

}
