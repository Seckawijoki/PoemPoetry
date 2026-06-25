---
name: Ink & Parchment
colors:
  surface: '#fef9ea'
  surface-dim: '#dedacb'
  surface-bright: '#fef9ea'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f8f3e4'
  surface-container: '#f3eedf'
  surface-container-high: '#ede8d9'
  surface-container-highest: '#e7e2d4'
  on-surface: '#1d1c13'
  on-surface-variant: '#574240'
  inverse-surface: '#323127'
  inverse-on-surface: '#f6f1e2'
  outline: '#8a716f'
  outline-variant: '#dec0bd'
  surface-tint: '#a63937'
  primary: '#761519'
  on-primary: '#ffffff'
  primary-container: '#962d2d'
  on-primary-container: '#ffb3ae'
  inverse-primary: '#ffb3ae'
  secondary: '#625e50'
  on-secondary: '#ffffff'
  secondary-container: '#e8e2d0'
  on-secondary-container: '#686456'
  tertiary: '#3f3b30'
  on-tertiary: '#ffffff'
  tertiary-container: '#575246'
  on-tertiary-container: '#cdc6b6'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#ffdad7'
  primary-fixed-dim: '#ffb3ae'
  on-primary-fixed: '#410005'
  on-primary-fixed-variant: '#862122'
  secondary-fixed: '#e8e2d0'
  secondary-fixed-dim: '#ccc6b5'
  on-secondary-fixed: '#1e1c11'
  on-secondary-fixed-variant: '#4a473a'
  tertiary-fixed: '#e9e2d2'
  tertiary-fixed-dim: '#cdc6b6'
  on-tertiary-fixed: '#1e1b12'
  on-tertiary-fixed-variant: '#4b463b'
  background: '#fef9ea'
  on-background: '#1d1c13'
  surface-variant: '#e7e2d4'
typography:
  display-lg:
    fontFamily: Noto Serif
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 64px
    letterSpacing: 0.1em
  headline-lg:
    fontFamily: Noto Serif
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 44px
  headline-lg-mobile:
    fontFamily: Noto Serif
    fontSize: 28px
    fontWeight: '600'
    lineHeight: 36px
  title-md:
    fontFamily: Noto Serif
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  body-lg:
    fontFamily: Noto Serif
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 32px
  body-md:
    fontFamily: Noto Serif
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  label-sm:
    fontFamily: Source Sans 3
    fontSize: 12px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.05em
spacing:
  margin-page: 2rem
  gutter-grid: 1.5rem
  stack-sm: 0.5rem
  stack-md: 1.5rem
  stack-lg: 3rem
---

## Brand & Style
The design system is rooted in the "Shanshui" (literati) aesthetic, prioritizing intellectual tranquility and the poetic cadence of traditional Chinese scrolls. It targets a scholarly audience seeking a focused, premium, and culturally resonant experience.

The visual style is **Minimalist / Textural**, characterized by:
- **Spatial Breathability:** Ample white space (yubai) to allow content to "breathe" like a calligraphic composition.
- **Scholarly Sophistication:** Use of subtle textures and a refined color palette to evoke the feeling of ink on rice paper.
- **Linear Elegance:** Thin, intentional lines and borders that serve as structural guides without overwhelming the content.
- **Rhythmic Order:** A layout that feels balanced and grounded, reflecting the disciplined nature of classical poetry.

## Colors
The palette is inspired by traditional ink painting and scholar’s seals.

- **Primary (Cinnabar Red):** Used for key actions, active states, and emphasis, mimicking the "seal" (yinzhang) on a scroll.
- **Secondary (Silk Parchment):** Used for container backgrounds and secondary buttons to provide a soft contrast against the main background.
- **Tertiary (Charcoal Ink):** Used for supporting text and subtle iconography to maintain a high-end, low-glare reading experience.
- **Neutral (Rice Paper):** The foundational canvas color, providing warmth and a natural texture.

Backgrounds should avoid pure white; use `#F5F0E1` for the main canvas and `#FFFFFF` only for focused card elements or input fields to create a "layered paper" effect.

## Typography
The typography utilizes elegant Mingti/Songti serifs to convey a literary tone.

- **Character Spacing:** Increase letter-spacing for headlines and display text (0.05em to 0.1em) to emulate the rhythmic placement of calligraphy.
- **Line Height:** Generous line heights are required for body text (minimum 1.6x) to ensure legibility and a relaxed reading pace.
- **Hierarchical Contrast:** Use weight and color (Charcoal Ink vs. Cinnabar Red) rather than size alone to distinguish between levels.
- **Sans-Serif Utility:** A clean sans-serif is used sparingly for labels and metadata to provide modern clarity without detracting from the poetic serif-led headers.

## Layout & Spacing
The layout follows a **Fixed Grid** approach for desktop and tablet, and a **Fluid Margin** approach for mobile, emphasizing vertical flow.

- **Symmetry:** Layouts should be centered and symmetrical where possible to evoke the balance of traditional architecture.
- **Vertical Rhythm:** Use the "Stack" units to maintain consistent vertical spacing between blocks of poetry and UI controls.
- **Margins:** High-density content is discouraged. Page margins should be significant (32px minimum on mobile) to create a "frame" effect.
- **Breakpoints:**
  - **Mobile:** Single column, 16px horizontal padding.
  - **Tablet:** Centered 8-column grid (max-width 768px).
  - **Desktop:** Centered 12-column grid (max-width 1120px) with decorative side borders.

## Elevation & Depth
Depth in this design system is achieved through **Tonal Layers** rather than shadows.

- **Flattened Stack:** Elements should feel like sheets of paper resting atop one another. 
- **Backdrop Fills:** Distinguish elevated containers (like cards) by changing the background color to a slightly brighter or more textured parchment tone rather than using a drop shadow.
- **Subtle Borders:** Use 0.5pt to 1pt borders in `#706B5E` (with 20% opacity) to define boundaries. 
- **Ink Bleed:** For active states, use a soft, primary-colored outer glow (blur: 4px) to simulate ink diffusing into paper, but keep this effect rare and subtle.

## Shapes
The shape language is strictly **Sharp (0px)** or **Low-Radius (2px)** to reflect the precision of woodblock printing and scholar’s tools.

- **Primary Buttons:** Strictly rectangular (Sharp) to feel architectural and authoritative.
- **Secondary/System Controls:** May use a minimal 2px radius to feel slightly softer and more approachable.
- **Dividers:** Use thin horizontal or vertical lines. Occasionally, use a small "diamond" or "scroll" motif at the center of a divider to mark section breaks.

## Components
- **Buttons (Primary):** Solid `#962D2D` background with `#F5F0E1` text. No rounded corners. Large, readable serif type.
- **Buttons (Secondary):** Outlined with `#962D2D` or filled with `#E8E2D0`. Use for navigation or "back" actions.
- **Cards:** Use a slightly lighter paper tone than the background. Borders should be subtle and thin.
- **Selection Chips:** Rectangular containers. Selected state uses a Cinnabar Red border or a full color fill with white text.
- **Input Fields:** Bottom-border only (underline style) to mimic the writing lines in traditional practice books.
- **Motifs:** Incorporate subtle decorative corners or "window lattice" (chuanhua) patterns in the corners of major containers to reinforce the cultural theme.
- **Progress Bars:** Use a simple horizontal line where the filled portion is Cinnabar Red and the unfilled portion is a light charcoal.