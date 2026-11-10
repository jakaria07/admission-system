namespace AdmissionSystem.Application.Common;

public sealed class NotFoundException : Exception
{
    public string Code { get; }

    public NotFoundException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public sealed class ConflictException : Exception
{
    public string Code { get; }

    public ConflictException(string code, string message) : base(message)
    {
        Code = code;
    }
}

