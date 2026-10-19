# Poner la web en línea

Todo es gratis: Supabase (base y cuentas), Vercel (el sitio), Discord y Google (iniciar sesión). Hay que hacerlo una
sola vez. Donde dice `<proyecto>` va el identificador que Supabase le da a tu proyecto (lo ves en su dirección,
`https://<proyecto>.supabase.co`).

## 1. Supabase: la base de datos

1. Entrá a <https://supabase.com> y tocá **Start your project**. Iniciá sesión con GitHub.
2. Tocá **New project**:
   - **Name:** `aoe2-bo-coach`
   - **Database Password:** tocá **Generate a password** y guardala en algún lado (no la vas a necesitar para la web, pero
     sí para administrar la base).
   - **Region:** South America (São Paulo), la más cerca.
   - Plan **Free**. Tocá **Create new project** y esperá a que termine (un par de minutos).
3. En el menú de la izquierda, entrá a **SQL Editor** y tocá **New query**.
4. Abrí en la PC el archivo `web/supabase/migrations/20261016000000_biblioteca.sql`, copiá todo y pegalo en el editor.
   Tocá **Run**. Abajo tiene que decir **Success. No rows returned**.
5. Andá a **Project Settings** (el engranaje) → **API Keys** y copiá:
   - la **Project URL** (`https://<proyecto>.supabase.co`; también está en **Data API**),
   - la **Publishable key** (empieza con `sb_publishable_`). La **Secret key** no se usa: no la copies a ningún lado.
6. En la PC, copiá `web/.env.example` como `web/.env.local` y pegá esos dos valores.

> El plan gratis de Supabase pausa el proyecto si pasa una semana sin que nadie lo use. Se reactiva desde su panel
> con **Restore project**.

## 2. Discord: iniciar sesión con Discord

1. Entrá a <https://discord.com/developers/applications> y tocá **New Application**. Nombre: `AoE2 BO Coach`.
2. En el menú de la izquierda, **OAuth2**:
   - copiá el **Client ID**,
   - tocá **Reset Secret** y copiá el **Client Secret**,
   - en **Redirects**, tocá **Add Redirect**, pegá `https://<proyecto>.supabase.co/auth/v1/callback` y **Save Changes**.
3. En Supabase: **Authentication** → **Sign In / Providers** → **Discord**. Activalo, pegá el Client ID y el Client
   Secret y tocá **Save**.

## 3. Google: iniciar sesión con Google

1. Entrá a <https://console.cloud.google.com> y creá un proyecto nuevo (arriba a la izquierda, selector de proyectos →
   **Proyecto nuevo**). Nombre: `AoE2 BO Coach`.
2. Buscá **Google Auth Platform** (o **Pantalla de consentimiento de OAuth**) y tocá **Comenzar**: nombre de la app
   `AoE2 BO Coach`, tu mail de soporte, público **Externo**, y terminá el asistente.
3. Andá a **Clientes** (o **Credenciales** → **Crear credenciales** → **ID de cliente de OAuth**):
   - **Tipo de aplicación:** Aplicación web.
   - **URIs de redireccionamiento autorizados:** `https://<proyecto>.supabase.co/auth/v1/callback`.
   - Tocá **Crear** y copiá el **ID de cliente** (termina en `.apps.googleusercontent.com`) y el **Secreto del
     cliente** (empieza con `GOCSPX-`). El secreto puede no volver a mostrarse.
4. En Supabase: **Authentication** → **Sign In / Providers** → **Google**. Activalo, pegá el ID y el secreto y tocá
   **Save**. Ojo: en **Client IDs** va el ID de cliente, no el nombre del proyecto (si no, no se guarda y Google queda
   apagado).
5. Para que pueda entrar cualquiera: en **Desarrollo de la marca** cargá la página principal (la de Vercel) y la
   política de privacidad (`https://<tu-sitio>.vercel.app/privacidad`), y en **Público** tocá **Publicar app**. Mientras
   esté en **Prueba**, solo entran los mails de **Usuarios de prueba**.

## 4. Vercel: el sitio

1. Entrá a <https://vercel.com> y tocá **Sign Up** → **Continue with GitHub** (plan **Hobby**).
2. Tocá **Add New…** → **Project** y elegí el repo `Kyriokes/aoe2-bo-coach` (si no aparece, tocá **Adjust GitHub App
   Permissions** y dale acceso a ese repo).
3. Antes de **Deploy**:
   - **Root Directory:** tocá **Edit** y elegí `web`.
   - **Framework Preset:** Next.js (lo detecta solo). El comando de build lo toma de `web/vercel.json`: instala .NET para
     compilar el motor a WebAssembly, así que el primer build tarda unos minutos.
   - **Environment Variables:** agregá `NEXT_PUBLIC_SUPABASE_URL` y `NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY` con los
     valores de Supabase. Si más adelante usás un dominio propio, agregá también `NEXT_PUBLIC_SITE_URL` con esa
     dirección (si no, los enlaces de la vista previa al compartir y el sitemap usan la de Vercel).
4. Tocá **Deploy**. Al terminar, Vercel te da la dirección del sitio (por ejemplo `https://aoe2-bo-coach.vercel.app`).
   El sitio de producción sale de la rama `main`.
5. De vuelta en Supabase: **Authentication** → **URL Configuration**:
   - **Site URL:** la dirección de Vercel, entera y con `https://`. Con una mal escrita (pasó `ttps://`), el login
     vuelve en loop a la página de Google o de Discord.
   - **Redirect URLs:** agregá `https://<tu-sitio>.vercel.app/auth/callback` y `http://localhost:3000/auth/callback`.

   Para revisarlo sin el panel: `curl -sD - "https://<proyecto>.supabase.co/auth/v1/callback?error=x&state=x"` muestra
   el Site URL en la línea `Location`.

## 5. Conectar la app

1. En la web, iniciá sesión y entrá a **Mi cuenta** → **Crear token**. Copiá el token (se muestra una sola vez).
2. En BO Coach, en la biblioteca, tocá **Cuenta de la web…**:
   - **Dirección de la web:** la de Vercel (si es otra que `https://aoe2-bo-coach.vercel.app`).
   - **Token de la app:** pegalo.
   - Tocá **Registrar enlaces bocoach://** para que el botón «Abrir en BO Coach» de la web abra la app.
   - **Guardar**.
3. Probá: abrí una BO en el editor, tocá **Publicar en la web** y abrí su página. En otra PC (con BO Coach instalado),
   el botón **Abrir en BO Coach** de esa página la importa directo.
