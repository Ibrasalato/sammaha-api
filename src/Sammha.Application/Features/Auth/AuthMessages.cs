using Sammha.Application.Common;

namespace Sammha.Application.Features.Auth;

public record GetCurrentUserQuery : IQuery<UserDto?>;
public record SignUpCommand(SignUpInput Input) : ICommand<AuthResult>;
public record StartSignInCommand(string Login, string Password, string Portal) : ICommand<OtpChallengeDto>;
public record CompleteSignInCommand(string Challenge, string Code) : ICommand<AuthResult>;
public record ResendOtpCommand(string Challenge) : ICommand<OtpChallengeDto>;
public record ChangePasswordCommand(string Current, string Next) : ICommand<MediatR.Unit>;
public record UpdateProfileCommand(string? AvatarUrl, bool AvatarSet, bool ShowAvatar) : ICommand<UserDto>;
public record UpdateAccountCommand(string FullName, string? NationalId) : ICommand<UserDto>;
public record RequestEmailChangeCommand(string Email) : ICommand<OtpChallengeDto>;
public record ConfirmEmailChangeCommand(string Challenge, string Code) : ICommand<UserDto>;
public record RequestPhoneChangeCommand(string Phone) : ICommand<OtpChallengeDto>;
public record ConfirmPhoneChangeCommand(string Challenge, string Code) : ICommand<UserDto>;
