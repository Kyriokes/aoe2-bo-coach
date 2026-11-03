"use client";

import NextLink from "next/link";
import type { ComponentProps } from "react";
import { useI18n } from "./client";

/** next/link con la ruta en el idioma de la página: toda ruta interna va por acá. */
export default function Link({ href, ...props }: ComponentProps<typeof NextLink>) {
  const { href: inLocale } = useI18n();
  return <NextLink href={typeof href === "string" ? inLocale(href) : href} {...props} />;
}
