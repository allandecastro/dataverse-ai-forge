export interface ThemeTokens {
  bg0: string;
  bg1: string;
  bg2: string;
  bg3: string;
  bg4: string;
  border: string;
  borderLt: string;
  borderHi: string;
  t1: string;
  t2: string;
  t3: string;
  t4: string;
  codeBg: string;
  codeText: string;
  /** Deep-teal brand accent. `accent` = fill, `accentInk` = accent as text/icon, `accentFg` = text on accent fill. */
  accent: string;
  accentInk: string;
  accentFg: string;
  green: string;
  greenBg: string;
  orange: string;
  orangeBg: string;
  red: string;
  redBg: string;
  blue: string;
  blueBg: string;
  purple: string;
  purpleBg: string;
}

// NeuralForge — flat / minimal / elegant. IBM Plex + deep teal, no radius, no gradients.
// Semantic hues map to the design system's status ramp:
//   green→ok, orange→warn, red→danger, blue→info, purple→special.

export const darkTheme: ThemeTokens = {
  bg0: "#0a0b0e",
  bg1: "#111317",
  bg2: "#181b20",
  bg3: "#20242b",
  bg4: "#2e343d",
  border: "#22272f",
  borderLt: "#191d23",
  borderHi: "#2e343d",
  t1: "#eef0f3",
  t2: "#a8adb8",
  t3: "#6b727e",
  t4: "#474d57",
  codeBg: "#0c0d10",
  codeText: "#9fd4b0",
  accent: "#0f8a7a",
  accentInk: "#2fb3a1",
  accentFg: "#ffffff",
  green: "#42b48a",
  greenBg: "rgba(66,180,138,0.13)",
  orange: "#d98f4a",
  orangeBg: "rgba(217,143,74,0.13)",
  red: "#dd6f6f",
  redBg: "rgba(221,111,111,0.13)",
  blue: "#5d8ed1",
  blueBg: "rgba(93,142,209,0.13)",
  purple: "#9b86d6",
  purpleBg: "rgba(155,134,214,0.13)",
};

export const lightTheme: ThemeTokens = {
  bg0: "#fbfbfc",
  bg1: "#ffffff",
  bg2: "#f4f5f7",
  bg3: "#eceef1",
  bg4: "#e2e5ea",
  border: "#e7e9ed",
  borderLt: "#eef0f3",
  borderHi: "#d6d9df",
  t1: "#16181d",
  t2: "#525861",
  t3: "#878d96",
  t4: "#b1b6be",
  codeBg: "#15171c",
  codeText: "#9fd4b0",
  accent: "#0f8a7a",
  accentInk: "#0c7165",
  accentFg: "#ffffff",
  green: "#0e8a64",
  greenBg: "rgba(14,138,100,0.10)",
  orange: "#bf7220",
  orangeBg: "rgba(191,114,32,0.10)",
  red: "#cc5050",
  redBg: "rgba(204,80,80,0.10)",
  blue: "#3a6bc4",
  blueBg: "rgba(58,107,196,0.10)",
  purple: "#6f57c0",
  purpleBg: "rgba(111,87,192,0.10)",
};
