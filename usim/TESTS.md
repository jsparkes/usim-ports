<title>Test protocols</title>

# Keyboard

To test that the keyboard drivers work; see that alphabetic characters
work in the Listener, and that bucky keys work in Zmacs.

If Top (or Greek if Cadet) are mapped, see that those insert the
proper shifted key.

In the listener:

  - Top-Z: should insert the alpha character.

In Zmacs:

  - C-x: This should ask prompt for "Control-X:", hit C-g.
  
  - C-x h: This should mark the whole buffer.
  
  - C-x C-h: This should report "Control-X Control-H not a defined
    key".

  - M-x: This should ask for an extended command.  Hit C-g to get out.

  - Top-H: This should invoke the helper, then press C and press
    C-M-K.  This should report that the command does "Kill one or more
    s-experssions forward".

## Cadet specific tests

See that 'kbd' is set to 'cadet' in usim.ini.
