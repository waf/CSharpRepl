# csharprepl website

Source for the promotional site at <https://fuqua.io/CSharpRepl>.

This is a plain static site — no build step, no Jekyll. GitHub Pages serves the
files in this branch as-is (the `.nojekyll` file disables Jekyll processing).

## Editing

Everything is hand-editable HTML and CSS:

- `index.html` — the whole page (markup + a little inline JS for copy / lightbox / reveal).
- `style.css` — the design system. Colours, type, and spacing live in the `:root`
  variables at the top of the file.
- `404.html` — self-contained (inline styles) so it renders at any URL depth.
- `images/` — screenshots and the intro video.

To preview locally, open `index.html` in a browser, or serve the folder:

```console
python -m http.server
```

## Deploying

Push to the `gh-pages` branch. GitHub Pages publishes it automatically.
