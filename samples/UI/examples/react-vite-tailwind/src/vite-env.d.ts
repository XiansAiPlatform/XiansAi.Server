/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_XIANS_SERVER_URL: string;
  readonly VITE_XIANS_TENANT_ID?: string;
  readonly VITE_OIDC_AUTHORITY: string;
  readonly VITE_OIDC_CLIENT_ID: string;
  readonly VITE_OIDC_SCOPE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
