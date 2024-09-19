using System;

namespace BoletoNetCore.Exceptions;

public class TokenNotFoundException : Exception
{
    public TokenNotFoundException(string loginUrl)
    {
        this.LoginUrl= loginUrl;
        
    }
    
    public string LoginUrl { get; set; }
}