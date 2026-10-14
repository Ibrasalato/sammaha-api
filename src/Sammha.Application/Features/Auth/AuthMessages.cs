using Sammha.Application.Common;

namespace Sammha.Application.Features.Auth;

public record GetCurrentUserQuery : IQuery<UserDto?>;
public record SignUpCommand(SignUpInput Input) : ICommand<AuthResult>;
public record StartSignInCommand(string Login, string Password, string Portal) : ICommand<OtpChallengeDto>;
public record CompleteSignInCommand(string Challenge, string Code) : ICommand<AuthResult>;
public record ResendOtpCommand(string Challenge) : ICommand<OtpChallengeDto>;
/// <summary>ends every other session and returns a fresh one for this device</summary>
public record ChangePasswordCommand(string Current, string Next) : ICommand<AuthResult>;
/// <summary>exchange a refresh token for a new access + refresh pair (rotation)</summary>
public record RefreshSessionCommand(string RefreshToken) : ICommand<AuthResult>;
public record SignOutCommand(string? RefreshToken, bool Everywhere) : ICommand<MediatR.Unit>;
public record UpdateProfileCommand(string? AvatarUrl, bool AvatarSet, bool ShowAvatar) : ICommand<UserDto>;
public record UpdateAccountCommand(string FullName, string? NationalId) : ICommand<UserDto>;
public record UpdateAddressCommand(AddressInput Input) : ICommand<UserDto>;
public record UpdatePersonalCommand(PersonalInput Input) : ICommand<UserDto>;
public record RequestEmailVerificationCommand : ICommand<OtpChallengeDto>;
public record ConfirmEmailVerificationCommand(string Challenge, string Code) : ICommand<UserDto>;
public record RequestEmailChangeCommand(string Email) : ICommand<OtpChallengeDto>;
public record ConfirmEmailChangeCommand(string Challenge, string Code) : ICommand<UserDto>;
public record RequestPhoneChangeCommand(string Phone) : ICommand<OtpChallengeDto>;
public record ConfirmPhoneChangeCommand(string Challenge, string Code) : ICommand<UserDto>;
