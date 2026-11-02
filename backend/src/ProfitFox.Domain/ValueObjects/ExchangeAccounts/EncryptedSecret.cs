namespace ProfitFox.Domain.ValueObjects.ExchangeAccounts;

/// <summary>A secret encrypted with AES-256-GCM: a 12-byte nonce, the ciphertext and a 16-byte tag.</summary>
public sealed record EncryptedSecret(byte[] Nonce, byte[] Ciphertext, byte[] Tag);