todo optimization:

[1] the text rendering will submit command buffer many time when rendering large amount of text.

Try use multiple buffer that store the text and submit them at once.
