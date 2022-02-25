namespace PRo3D.Provenance

type 'a Decision =
    | Decided of 'a
    | Undecided of 'a

module Decision =
    let map f = function
        | Undecided x -> f x
        | x -> x

    let get = 
        function
        | Decided x 
        | Undecided x -> x
