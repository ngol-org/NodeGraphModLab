import * as esbuild from "esbuild";

await esbuild.build({
  entryPoints: ["src/index.ts"],
  bundle: true,
  platform: "node",
  target: "node18",
  format: "esm",
  outfile: "dist/bundle.js",
  // CJS パッケージ（ws 等）が require('events') 等を呼ぶため、
  // ESM バンドル内で require を使えるよう createRequire を注入する
  banner: {
    js: "import{createRequire}from'module';const require=createRequire(import.meta.url);",
  },
});
