# Auth setup (Auth.js / NextAuth v5)

The `users` table already exists (from the migrations). What's missing is login. I'm giving you
this as a guided drop-in rather than pre-wiring it, for one honest reason: **auth needs *your*
provider choice and secret keys** (Google client ID, or an email provider, etc.). Wiring it "half
on" with placeholder keys would just make the app throw at runtime. Follow these steps — an AI
assistant can execute them with you in a few minutes.

## Recommended: Auth.js v5 with Google (fastest for a solo SaaS)

### 1. Install
```bash
npm install next-auth@beta --workspace apps/web
```

### 2. Env
Add to `.env`:
```
AUTH_SECRET=            # generate: npx auth secret
AUTH_GOOGLE_ID=         # from Google Cloud console → OAuth credentials
AUTH_GOOGLE_SECRET=
```

### 3. `apps/web/auth.ts`
```ts
import NextAuth from "next-auth";
import Google from "next-auth/providers/google";

export const { handlers, auth, signIn, signOut } = NextAuth({
  providers: [Google],
  callbacks: {
    // On first sign-in, upsert into your tenants/users tables here.
    async signIn() { return true; },
  },
});
```

### 4. `apps/web/app/api/auth/[...nextauth]/route.ts`
```ts
import { handlers } from "@/auth";
export const { GET, POST } = handlers;
```

### 5. Protect a page / read the session
```ts
import { auth } from "@/auth";

export default async function Dashboard() {
  const session = await auth();
  if (!session) { /* redirect to sign in */ }
  return <div>Signed in as {session?.user?.email}</div>;
}
```

### 6. Sign-in button (client)
```tsx
"use client";
import { signIn, signOut } from "next-auth/react";
export function AuthButton({ signedIn }: { signedIn: boolean }) {
  return signedIn
    ? <button onClick={() => signOut()}>Sign out</button>
    : <button onClick={() => signIn("google")}>Sign in with Google</button>;
}
```

### 7. Link auth → your data
On first sign-in, create (or find) a `tenants` row for the user and a `users` row
(`tenant_id`, `email`, `role: "trader"`). From then on, every `accounts` / `trades` / `rules`
query filters by that `tenant_id`. That's your multi-tenant isolation — the model's already built.

## Alternative: Clerk
If you'd rather not manage auth at all, Clerk is more turnkey (drop-in `<SignIn/>`, hosted UI).
Trade-off: another paid dependency and less control. For a bootstrapped tool, Auth.js + Google is
the leaner choice.

## Note on versions
This repo pins Next 16 / React 19 to match Velocity. `next-auth@beta` supports them, but if you hit
peer-dependency friction, pin the web app to Next 15 / React 18 — auth tooling is most battle-tested
there today.
