import { describe, expect, it } from "vitest";
import { jsonLdText } from "./jsonLd";

describe("datos estructurados", () => {
  it("un nombre con </script> no cierra la etiqueta", () => {
    const text = jsonLdText({ name: "</script><script>alert(1)</script>" });
    expect(text).not.toContain("<");
    expect(JSON.parse(text)).toEqual({ name: "</script><script>alert(1)</script>" });
  });
});
