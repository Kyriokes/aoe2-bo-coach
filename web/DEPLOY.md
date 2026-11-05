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
   Tocá **Run**. Abajo tiene que decir **Success. No rows returned**. Repetí lo mismo, en orden de nombre, con cada uno
   de los demás archivos de esa carpeta, cada uno en una consulta nueva. Cuando aparezca un archivo nuevo en la carpeta,
   se corre solo ese, **antes** de publicar la web que lo necesita. El de los registros (`20261102000000_registros.sql`)
   crea el bucket privado `registros`: mirá en **Storage** que esté, sin el cartel "Public".
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
     dirección (si no, los enlaces de la vista previa al compartir y el sitemap usan la de Vercel). Para Google Search
     Console, `GOOGLE_SITE_VERIFICATION` con el código de la etiqueta HTML.
4. Tocá **Deploy**. Al terminar, Vercel te da la dirección del sitio (por ejemplo `https://aoe2-bo-coach.vercel.app`).
   El sitio de producción sale de la rama `main`.
5. De vuelta en Supabase: **Authentication** → **URL Configuration**:
   - **Site URL:** la dirección de Vercel, entera y con `https://`. Con una mal escrita (pasó `ttps://`), el login
     vuelve en loop a la página de Google o de Discord.
   - **Redirect URLs:** agregá `https://<tu-sitio>.vercel.app/auth/callback` y `http://localhost:3000/auth/callback`.

   Para revisarlo sin el panel: `curl -sD - "https://<proyecto>.supabase.co/auth/v1/callback?error=x&state=x"` muestra
   el Site URL en la línea `Location`.

## 5. Conectar la app

1. En BO Coach, tocá **Conectar cuenta** (en la sección Build order): se abre **Mi cuenta** en el navegador. Ahí tocá
   **Conectar la app** y aceptá si el navegador pregunta si abre BO Coach. La app tiene que decir "Conectada como
   <tu nombre>" y mostrar tu lista de BO.
2. Si no se abre, copiá el token que muestra la web. En la app: **Ajustes**, pestaña **Avanzado**, **Dirección y
   token…**, y pegalo. Ahí también va la dirección de la web, si es otra que `https://aoe2-bo-coach.vercel.app`.
3. Probá: en la web, en una BO pública, tocá **Usar en el coach**, y en la app tocá **Actualizar lista**: tiene que
   aparecer. El botón **Abrir en BO Coach** de la página de una BO la suma directo.

Las BO se arman en la web (`/subir`). Para publicar muchas de una vez con tu cuenta hay `BoCoach.exe --publish <carpeta>`
(`--dry-run` primero, si querés ver qué haría). Ese comando pide un token con permiso de publicar: creá uno con
**Crear token** en Mi cuenta y pegalo en **Dirección y token…**, porque el de "Conectar la app" no publica.