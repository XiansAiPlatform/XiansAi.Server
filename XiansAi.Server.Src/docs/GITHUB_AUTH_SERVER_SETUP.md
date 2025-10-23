# GitHub Auth - Server Config Snippets

Only the required configuration snippets for appsettings.json and .env, for both dev (HMAC) and prod (RSA).

Note:
- Use either HMAC (dev) or RSA (prod), not both at the same time.
- For JSON, keep PEM on a single line with literal \n characters.

---

## appsettings.json (Dev: HMAC)

```json
{
  "AuthProvider": {
    "Provider": "GitHub"
  },
  "GitHub": {
    "ClientId": "your_client_id",
    "ClientSecret": "your_client_secret",
    "RedirectUri": "http://localhost:3000/auth/callback/github",
    "Scopes": "read:user user:email",

    "JwtIssuer": "http://localhost:8080/auth",
    "JwtAudience": "xiansai-api",
    "JwtAccessTokenMinutes": 60,

    "JwtHmacSecret": "dev-super-secret-at-least-64-bytes"
  }
}
```

## appsettings.json (Prod: RSA)

```json
{
  "AuthProvider": {
    "Provider": "GitHub"
  },
  "GitHub": {
    "ClientId": "your_prod_client_id",
    "ClientSecret": "your_prod_client_secret",
    "RedirectUri": "https://app.yourdomain.com/auth/callback/github",
    "Scopes": "read:user user:email",

    "JwtIssuer": "https://api.yourdomain.com/auth",
    "JwtAudience": "xiansai-api",
    "JwtAccessTokenMinutes": 60,

    "JwtPrivateKeyPem": "-----BEGIN PRIVATE KEY-----\n...your PKCS#8 RSA private key...\n-----END PRIVATE KEY-----",
    "JwtKeyId": "key-2025-01"
  }
}
```

## .env (Dev: HMAC)

```dotenv
AuthProvider__Provider=GitHub

GitHub__ClientId=your_client_id
GitHub__ClientSecret=your_client_secret
GitHub__RedirectUri=http://localhost:3000/auth/callback/github
GitHub__Scopes=read:user user:email

GitHub__JwtIssuer=http://localhost:8080/auth
GitHub__JwtAudience=xiansai-api
GitHub__JwtAccessTokenMinutes=60

GitHub__JwtHmacSecret=934b7125d7cd260cfe58fb47386858392b7d5fc57a643c8dc9a97a9eed3a5731c8a0731f84d558c70a40cb588ce5a1768b4564c8d81ef70a0ee3fa4f3a625d79
```

## .env (Prod: RSA)

```dotenv
AuthProvider__Provider=GitHub

GitHub__ClientId=your_prod_client_id
GitHub__ClientSecret=your_prod_client_secret
GitHub__RedirectUri=https://app.yourdomain.com/auth/callback/github
GitHub__Scopes=read:user user:email

GitHub__JwtIssuer=https://api.yourdomain.com/auth
GitHub__JwtAudience=xiansai-api
GitHub__JwtAccessTokenMinutes=60

GitHub__JwtPrivateKeyPem=-----BEGIN PRIVATE KEY-----\n...your PKCS#8 RSA private key...\n-----END PRIVATE KEY-----
GitHub__JwtKeyId=key-2025-01
```
