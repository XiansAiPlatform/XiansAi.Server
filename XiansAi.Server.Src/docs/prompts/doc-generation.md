# Doc Generation Prompt

## Quality of the documentation

Analyze the provided code samples and update the existing Markdown documentation accordingly. If no documentation exists, generate a new one. When updating, ensure that the documentation remains concise, relevant, and structured for ease of understanding.

- Identify and incorporate patterns from similar code samples to make the documentation more comprehensive and reusable.
- Ensure the documentation is generic enough to support future code generation while maintaining clarity and accuracy.
- Strictly document only the requested scope—avoid adding unrelated or unnecessary details.
- You may add examples, however do not replicate content in docs where you are able to refer to the file location in the docs.
- Document reference file locations as needed in the docs.

Always update `/XiansAi.Server.Docs/nonfunctional/index.md` with the document file if it is not existing in the index.

## Markdown Formatting

- Use markdown formatting for the documentation.
- Use code blocks for code samples.
- Use links for reference to other files.
- Use single space for bullet points.
- Add a blank lines before and after code blocks.
- Add a blank line before and after each heading.
- Add a blank line before and after each list.
