/**
 * Datos del proyecto de Supabase (públicos: la clave "publishable" solo permite lo que dejan los permisos por fila).
 * Se cargan en web/.env.local (ver .env.example) y en las variables del proyecto en Vercel.
 */
export const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL ?? "";
export const supabaseKey = process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY ?? "";

/** Sin proyecto configurado (por ejemplo en el CI) la web muestra la biblioteca vacía en vez de fallar. */
export const supabaseConfigured = supabaseUrl.length > 0 && supabaseKey.length > 0;
